using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading;
using System.ComponentModel;
using System.Windows;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace TPSNet.Desktop.Modules.TPS.ActiveBlocks.Collections
{
    /// <summary>
    /// Multithreaded observable generic collection
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ConcurrentObservable<T> : ICollection<T>, INotifyCollectionChanged, INotifyPropertyChanged where T : class
    {
        public int Count => mCollection.Count;

        public bool IsReadOnly => throw new NotImplementedException();

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        private List<T> mCollection;
        private int mUpdatePending = 0;
        private readonly object mEnumLock = new object();
        private readonly object mRaiseLock = new object();
        private readonly object mExclusiveLock = new object();

        public ConcurrentObservable()
        {
            mCollection = new List<T>();
        }

        public ConcurrentObservable(IEnumerable<T> items)
        {
            if (items != null)
                mCollection = new List<T>(items);
            else
                mCollection = new List<T>();
        }

        public void Add(T item)
        {
            lock (mExclusiveLock)
            {
                lock (mRaiseLock)
                {
                    lock (mEnumLock)
                        mCollection.Add(item);
                }
                RaiseCollectionChanged(NotifyCollectionChangedAction.Add, item);
            }
        }

        public void Clear()
        {
            lock (mExclusiveLock)
            {
                lock (mRaiseLock)
                {
                    lock (mEnumLock)
                        mCollection.Clear();
                }
                RaiseCollectionChanged(NotifyCollectionChangedAction.Reset);
            }
        }

        public bool Remove(T item)
        {
            int idx = -1;
            lock (mExclusiveLock)
            {
                lock (mRaiseLock)
                {
                    lock (mEnumLock)
                    {
                        idx = mCollection.IndexOf(item);
                        if (idx != -1)
                            mCollection.RemoveAt(idx);
                    }
                }
                if (idx != -1)
                    RaiseCollectionChanged(NotifyCollectionChangedAction.Remove, item, idx);
            }
            return idx != -1;
        }

        public bool Contains(T item)
        {
            lock (mEnumLock)
            {
                return mCollection.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (mEnumLock)
            {
                if (array.Length >= mCollection.Count)
                    mCollection.CopyTo(array, arrayIndex);
                else
                    mCollection.CopyTo(0, array, arrayIndex, array.Length - arrayIndex);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new SafeEnumerator<T>(mCollection, mEnumLock);
        }

        /// <summary>
        /// Removes list of items, fires Reset event
        /// </summary>
        /// <param name="items"></param>
        public void RemoveRange(IList<T> items)
        {
            lock (mExclusiveLock)
            {
                lock (mEnumLock)
                {
                    lock (mRaiseLock)
                    {
                        foreach (var i in items)
                        {
                            mCollection.Remove(i);
                        }
                    }
                }
                RaiseCollectionChanged(NotifyCollectionChangedAction.Reset);
            }
        }

        /// <summary>
        /// Adds list of items, fires Reset event
        /// </summary>
        /// <param name="items"></param>
        public void AddRange(IList<T> items)
        {
            lock (mExclusiveLock)
            {
                lock (mEnumLock)
                {
                    lock (mRaiseLock)
                    {
                        foreach (var i in items)
                        {
                            mCollection.Add(i);
                        }
                    }
                }
                RaiseCollectionChanged(NotifyCollectionChangedAction.Reset);
            }
        }

        /// <summary>
        /// Use isExclusive to block all other threads during update
        /// Don't use begin/endupdate if you need remove event
        /// </summary>
        /// <param name="isExclusive"></param>
        public void BeginUpdate(bool isExclusive = true)
        {
            if (isExclusive)
                Monitor.Enter(mExclusiveLock);
            Interlocked.Add(ref mUpdatePending, 1);
        }

        /// <summary>
        /// Perform RaiseCollectionChanged
        /// </summary>
        /// <param name="onlyAdd">true if you certain, that no items where removed during update</param>
        /// <param name="newItems"></param>
        public void EndUpdate(bool onlyAdd = false, object newItems = null)
        {
            if (mUpdatePending > 0)
                Interlocked.Add(ref mUpdatePending, -1);
            if (onlyAdd)
                RaiseCollectionChanged(NotifyCollectionChangedAction.Add, newItems);
            else
                RaiseCollectionChanged(NotifyCollectionChangedAction.Reset);
            if (Monitor.IsEntered(mExclusiveLock))
                Monitor.Exit(mExclusiveLock);
        }

        public object GetExclusiveLock()
        {
            return mExclusiveLock;
        }
        

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        void RaiseCollectionChanged(NotifyCollectionChangedAction action, object changedItems = null, int removedIndex = -1) 
        {
            if (mUpdatePending == 0)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => InvokeCollectionChanged(action, changedItems, removedIndex));
                //InvokeCollectionChanged(action, changedItems, removedIndex);
            }
        }

        void InvokeCollectionChanged(NotifyCollectionChangedAction action, object changedItems = null, int removedIndex = -1)
        {
            lock (mRaiseLock)
            {
                switch (action)
                {
                    case NotifyCollectionChangedAction.Reset:
                        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action));
                        break;
                    case NotifyCollectionChangedAction.Add:
                        if (changedItems is IList items)
                            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, items));
                        else
                            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, changedItems));
                        break;
                    case NotifyCollectionChangedAction.Remove:
                        if (changedItems is IList)
                            throw new NotImplementedException();
                        else
                            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(action, changedItems, removedIndex));
                        break;
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Count"));
            }
        }

        public class SafeEnumerator<G> : IEnumerator<G>
        {
            private readonly IEnumerator<G> mInner;
            private readonly object mLock;
            //SmartLocker SL;

            public SafeEnumerator(ICollection<G> inner, object enumlock)
            {
                mLock = enumlock;
                //SL = new SmartLocker();
                //SL.Enter(mLock);
                Monitor.Enter(mLock);
                mInner = inner.GetEnumerator();
            }

            public void Dispose()
            {
                mInner.Dispose();
                //SL.Dispose();
                Monitor.Exit(mLock);
            }

            public bool MoveNext()
            {
                return mInner != null && mInner.MoveNext();
            }

            public void Reset()
            {
                mInner.Reset();
            }

            public G Current
            {
                get { return mInner.Current; }
            }

            object IEnumerator.Current
            {
                get { return Current; }
            }

        }            

        class SmartLocker : IDisposable
        {
            public static string HoldingTrace;

            object Locker;

            public void Enter(object locker)
            {
                bool locked = false;
                int timeout = 0;
                Locker = locker;
                while (!locked)
                {
                    locked = Monitor.TryEnter(locker, 5000);
                    if (!locked)
                    {
                        timeout += 3000;
                    }
                }

                HoldingTrace = Thread.CurrentThread.ManagedThreadId + " - " + Environment.StackTrace;
            }

            public void Exit()
            {
                if (Monitor.IsEntered(Locker))
                    Monitor.Exit(Locker);
                HoldingTrace = "";
            }

            public void Dispose()
            {
                Exit();
            }
        }
    }

    static class ObservableExtensions
    {
        public static List<T> ToList<T>(this ConcurrentObservable<T> collection) where T : class
        {
            lock (collection.GetExclusiveLock())
            {
                return collection.ToList();
            }
        }
    }
}
