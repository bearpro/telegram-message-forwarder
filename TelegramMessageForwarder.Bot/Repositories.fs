module Repositories

open Types

type IPeerRepository<'user, 'peerInfo> =
    abstract member RegisterPeer: userId: 'user -> peerInfo: 'peerInfo -> Async<unit>
    abstract member GetPeers: userId: 'user -> Async<seq<'peerInfo>>

type IStateRepository<'user> =
    abstract member GetState: userId: 'user -> Async<State>
    abstract member SetState: userId: 'user -> state: State -> Async<unit>

type InMemoryPeerRepository<'user, 'peerInfo when 'user: comparison>() = 
    let mutable storage = Map.empty
    
    interface IPeerRepository<'user, 'peerInfo> with
        
        member _.RegisterPeer userId peerInfo =
            storage <- 
                if (storage.ContainsKey(userId)) then
                    storage.Add(userId, peerInfo :: storage[userId])
                else
                    storage.Add(userId, [peerInfo])
            async.Return ()
        
        member _.GetPeers userId =
            storage 
            |> Map.tryFind userId 
            |> Option.defaultValue []
            |> List.toSeq
            |> async.Return

type InMemoryStateRepository<'user when 'user: comparison>() =
    let mutable storage = Map.empty

    interface IStateRepository<'user> with
        member _.GetState userId =
            storage
            |> Map.tryFind userId
            |> Option.defaultValue Disabled
            |> async.Return
        
        member _.SetState userId state =
            storage <- storage.Add(userId, state)
            async.Return ()