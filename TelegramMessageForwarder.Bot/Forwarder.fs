module Forwarder

open System
open Serilog

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
    let apiId = Environment.GetEnvironmentVariable("TG_CLIENT_API_ID")
    let apiHash = Environment.GetEnvironmentVariable("TG_CLIENT_API_HASH")
    let phoneNumber = Environment.GetEnvironmentVariable("TG_PHONE_NUMBER")
    log.Information("Initializing client...")
    let client = new WTelegram.Client(fun key -> 
            match key with
            | "api_id" -> apiId
            | "api_hash" -> apiHash
            | "phone_number" -> phoneNumber
            | _ -> null)

    WTelegram.Helpers.Log <- (Action<int, string>(writeWtelegramLog log))

    let! user = client.LoginUserIfNeeded()
    log.Information($"User authorized, {user.first_name} {user.last_name}")
    return client
    }

open WTelegram
open TL
open System.Threading.Tasks

let rec resolveAllPeers (client: Client) contacts = 
    task {
        match contacts with 
        | h :: t -> 
            let! peer = 
                match h with
                | Types.Peer.Contact contact -> task {
                    let! contactPeer = client.Contacts_ResolvePhone(contact.PhoneNumber)
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
                |> Seq.find(fun x -> x.Date = update.Message.Date) 
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
