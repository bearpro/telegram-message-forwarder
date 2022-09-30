module Bot

open Telegram.Bot
open System
open Telegram.Bot.Types
open System.Threading
open System.Threading.Tasks
open Serilog
open Types
open Repositories
open System.Text

let getBot 
    (log : ILogger) 
    (stateRepo: IStateRepository<int64>)
    (peerRepo: IPeerRepository<int64, Peer>)
    (telegramClient: WTelegram.Client) =

    let token = Environment.GetEnvironmentVariable("BOT_TOKEN")

    let botClient = new TelegramBotClient(token)    

    let handleUpdateToForward (botClient: ITelegramBotClient) (update: Update) = 
        async {
            let userId = update.Message.From.Id
            let! peers = peerRepo.GetPeers(userId) 
            let! infoMessage = 
                botClient.SendTextMessageAsync(
                    update.Message.Chat.Id, 
                    $"Сообщения пересылаются...", 
                    replyToMessageId = update.Message.MessageId) 
                |> Async.AwaitTask
            do! Forwarder.forwardUpdate telegramClient update (Seq.toList peers) 
                |> Async.AwaitTask

            // TODO: Разобраться с этим
            //for infoMessageTomeRemain, emoji in Seq.zip [1..12] "🕛🕐🕑🕒🕓🕔🕕🕖🕗🕘🕙🕚" |> Seq.rev do
            //    let! _ = 
            //        botClient.EditMessageTextAsync(
            //            infoMessage.Chat.Id, 
            //            infoMessage.MessageId, 
            //            $"Сообщение переслано\n{emoji}{infoMessageTomeRemain}")
            //        |> Async.AwaitTask
            //    do! Async.Sleep (TimeSpan.FromSeconds 1)
            //let! _ = 
            //    botClient.DeleteMessageAsync(infoMessage.Chat.Id, infoMessage.MessageId) 
            //    |> Async.AwaitTask
            ()
        }
    
    let handleNewPeer (client: ITelegramBotClient) (update: Update) = 
        // TODO Добавлять группы
        async {
            if not (isNull update.Message) && not (isNull update.Message.Contact) then
                let peer = Peer.Contact update.Message.Contact
                do! peerRepo.RegisterPeer update.Message.From.Id peer
                do! client.SendTextMessageAsync(
                        update.Message.Chat.Id, 
                        "Контакт сохранён") 
                    |> Async.AwaitTask |> Async.Ignore

            elif not (isNull update.Message) 
                && not (isNull update.Message.ForwardFromChat)
                && not (isNull update.Message.ForwardFromChat.Title) then
                let peer = Peer.Channel update.Message.ForwardFromChat.Title
                do! peerRepo.RegisterPeer update.Message.From.Id peer
                do! client.SendTextMessageAsync(
                        update.Message.Chat.Id, 
                        $"Канал {update.Message.ForwardFromChat.Title} сохранён") 
                    |> Async.AwaitTask |> Async.Ignore
            
            elif not (isNull update.Message)
                && not (isNull update.Message.ForwardFrom)
                && update.Message.ForwardFrom.Id <> update.Message.From.Id
                && not (isNull update.Message.ForwardFrom.Username) then
                let peer = Peer.Username update.Message.ForwardFrom.Username
                do! peerRepo.RegisterPeer update.Message.From.Id peer
                do! client.SendTextMessageAsync(
                        update.Message.Chat.Id, 
                        $"Получатель с именем пользователя {update.Message.ForwardFrom.Username} сохранён") 
                    |> Async.AwaitTask |> Async.Ignore

            else 
                do! client.SendTextMessageAsync(update.Message.Chat.Id, $"Не удалось сохранить :(") 
                    |> Async.AwaitTask |> Async.Ignore

        }

    let handleListPeers (client: ITelegramBotClient) (update: Update) = 
        async {
            let userId = update.Message.From.Id
            let! peers = peerRepo.GetPeers(userId)
            let sb = new StringBuilder()
            sb.AppendLine("Список зарегистрированных контактов:") |> ignore
            for peer in peers do
                match peer with 
                | Contact contact ->
                    sb.AppendLine($"🧍 {contact.FirstName} {contact.LastName}") |> ignore
                | Channel title -> 
                    sb.AppendLine($"👯 {title}") |> ignore
                | Username username ->
                    sb.AppendLine($"👤 {username}") |> ignore

            do! client.SendTextMessageAsync(update.Message.Chat.Id, sb.ToString())
                |> Async.AwaitTask |> Async.Ignore
        }

    let handleUpdate 
        (client: ITelegramBotClient) 
        (update: Update) 
        (token: CancellationToken) = 

        async {
            let senderId = update.Message.From.Id
            let! currentState = stateRepo.GetState senderId
            
            if currentState = Disabled && update.Message.Text = "/start" then
                do! stateRepo.SetState senderId Enabled
                do! client.SendTextMessageAsync(update.Message.Chat.Id, "Бот запущен, настройте список контактов") 
                    |> Async.AwaitTask |> Async.Ignore
                ()

            if currentState = Disabled then ()

            elif currentState <> Disabled && update.Message.Text = "/stop" then
                do! stateRepo.SetState senderId Disabled

            elif currentState = Enabled && update.Message.Text = "/set_peers" then
                do! stateRepo.SetState senderId SetPeers
                do! client.SendTextMessageAsync(
                        update.Message.Chat.Id, 
                        "Вы можете прислать карточку контакта пользователя или " + 
                        "переслать любое сообщение из чата.\n" + 
                        "Если вы пришлёте сообщение из чата, то сообщения будут " +
                        "пересылаться именно в тот чат из которого вы переслали это сообщение!")
                    |> Async.AwaitTask |> Async.Ignore
                ()

            elif currentState = SetPeers && update.Message.Text = "/stop_set_peers" then
                do! stateRepo.SetState senderId Enabled
                do! client.SendTextMessageAsync(
                        update.Message.Chat.Id, 
                        "Все сообщения кроме команд будут пересланы указанным пользователям")
                    |> Async.AwaitTask |> Async.Ignore

            elif currentState = Enabled && update.Message.Text = "/list_peers" then 
                do! handleListPeers client update

            elif currentState = SetPeers then 
                do! handleNewPeer client update 

            elif currentState = Enabled && not (update.Message.Text.StartsWith('/')) then 
                do! handleUpdateToForward client update

            else
                do! client.SendTextMessageAsync(update.Message.Chat.Id, "Бот находится в неожиданном состоянии") 
                    |> Async.AwaitTask |> Async.Ignore
            
        } |> Async.StartAsTask :> Task

    let handleUpdateFunc = Func<ITelegramBotClient, Update, CancellationToken, Task>(handleUpdate)
    let errorHandlerFunc = Func<ITelegramBotClient, Exception, CancellationToken, Task>(fun _ e _ -> 
        log.Error(e, "Polling error")
        Task.CompletedTask)
    
    let botClient = task {
        do! botClient.SetMyCommandsAsync(seq {
            BotCommand(Command = "/start", Description = "Запускает бота")
            BotCommand(Command = "/set_peers", Description = "Переходит в режим сохранения контактов")
            BotCommand(Command = "/stop_set_peers", Description = "Завершает режим сохранения контактов")
            BotCommand(Command = "/list_peers", Description = "Отправляет сообщение со списком сохранённых контактов")
            BotCommand(Command = "/stop", Description = "Останавливает бота")

        })
        do (botClient.StartReceiving(handleUpdateFunc, errorHandlerFunc))
    
        let! me = botClient.GetMeAsync()
        log.Information $"Start listening for @{me.Username}"
        return botClient
    }

    botClient