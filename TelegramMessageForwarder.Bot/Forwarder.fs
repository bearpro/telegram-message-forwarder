module Forwarder

open System
open Serilog
open System.IO

let writeWtelegramLog (log: ILogger) level message =
    match level with
    | 0 -> log.Debug message
    | 1 -> log.Debug message
    | 2 -> log.Information message
    | 3 -> log.Warning message
    | 4 -> log.Error message
    | 5 -> log.Error message
    | _ -> log.Error message

let initTelegramClient (log: ILogger) = task {
    WTelegram.Helpers.Log <- (Action<int, string>(writeWtelegramLog log))
    
    let apiId = Environment.GetEnvironmentVariable("TG_CLIENT_API_ID")
    let apiHash = Environment.GetEnvironmentVariable("TG_CLIENT_API_HASH")
    let phoneNumber = Environment.GetEnvironmentVariable("TG_PHONE_NUMBER")
    
    let config key =
        match key with
            | "api_id" -> apiId
            | "api_hash" -> apiHash
            | "phone_number" -> phoneNumber
            | "session_pathname" -> null
            | _ -> null

    let client = new WTelegram.Client(config)
    
    let! user = client.LoginUserIfNeeded()
    log.Information($"Client authorized, {user.first_name} {user.last_name}")
    return client
    }

open WTelegram
open TL
open System.Threading.Tasks

let rec resolveAllPeers (client: Client) contacts = 
    task {
        let! allChats = client.Messages_GetAllChats()

        match contacts with 
        | peerInfo :: t -> 
            let! peer = 
                match peerInfo with
                | Types.Peer.Contact contact -> task {
                    let! contactPeer = client.Contacts_ResolvePhone(contact.PhoneNumber)
                    return InputPeerUser(contactPeer.User.ID, contactPeer.User.access_hash) :> InputPeer
                    }
                | Types.Peer.Channel title -> task {
                    let chat = 
                        allChats.chats.Values 
                        |> Seq.find(fun x -> x.Title = title)
                    return chat.ToInputPeer()
                    }
                | Types.Peer.Username username -> task {
                    let! contactPeer = client.Contacts_ResolveUsername(username)
                    return InputPeerUser(contactPeer.User.ID, contactPeer.User.access_hash) :> InputPeer
                    }
            let! restPeers = resolveAllPeers client t
            return peer :: restPeers
        | [] -> return []
    }

let forwardUpdate (client: Client) (update: Telegram.Bot.Types.Update) contacts = 
    if update.Message <> null then
        task {
            // TODO Кешировать пиры
            let! botPeer = client.Contacts_ResolveUsername("bearpro_message_forwarder_bot")
            let! history = client.Messages_GetHistory(botPeer, limit = 30)
            let messageToForward = 
                history.Messages 
                // TODO Проверить эту логику
                |> Seq.find(fun x -> x.Date = update.Message.Date && x.From <> null && x.From.ID <> botPeer.peer.ID) 
                :?> Message

            let! peers = resolveAllPeers client contacts

            for peer in peers do
                let! _ = client.Messages_ForwardMessages(
                    InputPeer.Self,
                    Array.singleton messageToForward.ID,
                    Array.singleton (WTelegram.Helpers.RandomLong()),
                    peer)
                ()
        }
    else Task.FromResult ()
