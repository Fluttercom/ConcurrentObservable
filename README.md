# ConcurrentObservable
Concurrent observable collection for WPF.
This collection allows to add or remove items in any non-UI thread with proper CollectionChanged behavior.

## Usage notes:
- NotifyCollectionChangedAction.Remove supported only by calling the Remove() method.
- If you use RemoveRange or AddRange, you will get NotifyCollectionChangedAction.Reset event.
- You can combine several changes by using BeginUpdate / EndUpdate methods:
> myCollection.BeginUpdate(true) //use true to get exclusive access to the current thread until you call EndUpdate()
> ...add or remove items
Call EndUpdate with onlyAdd=true in order to get NotifyCollectionChangedAction.Add if you sure that no items have been removed after BeginUpdate call. Also you need to provide the list of added items. Otherwise you will get NotifyCollectionChangedAction.Reset.
> myCollection.EndUpdate(true, addedItems)


