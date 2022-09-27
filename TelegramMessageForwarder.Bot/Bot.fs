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
            let peers = peers |> Seq.toList
            let peerCount = peers |> Seq.length
            do! Forwarder.forwardUpdate telegramClient update peers |> Async.AwaitTask
            // NOTE Не факт что это нужно
            let! _ = 
                botClient.SendTextMessageAsync(update.Message.Chat.Id, $"Сообщение переслано {peerCount} контактам.") 
                |> Async.AwaitTask
            ()
        }
    
    let handleNewPeer (client: ITelegramBotClient) (update: Update) = 
        // TODO Добавлять ботов и группы
        async {
            if (not (isNull update.Message) && not (isNull update.Message.Contact)) then
                do! peerRepo.RegisterPeer update.Message.From.Id (Peer.Contact update.Message.Contact)
                let! _ = 
                    client.SendTextMessageAsync(update.Message.Chat.Id, "Контакт сохранён") 
                    |> Async.AwaitTask
                ()
            ()
        }

    let handleListPeers (client: ITelegramBotClient) (update: Update) = 
        async {
            let userId = update.Message.From.Id
            let! peers = peerRepo.GetPeers(userId)
            let sb = new StringBuilder()
            let _ = sb.AppendLine("Список зарегистрированных контактов:")
            for peer in peers do
                match peer with 
                | Contact contact ->
                    let _ = sb.AppendLine($"🧍 {contact.FirstName} {contact.LastName}")
                    ()

            let _ = 
                client.SendTextMessageAsync(update.Message.Chat.Id, sb.ToString())
                |> Async.AwaitTask
            ()
        }

    let handleUpdate 
        (client: ITelegramBotClient) 
        (update: Update) 
        (token: CancellationToken) = 

        async {
            let senderId = update.Message.From.Id
            let! currentState = stateRepo.GetState senderId
            
            if (currentState = Disabled && update.Message.Text = "/start") then
                do! stateRepo.SetState senderId Enabled
                let! _ = 
                    client.SendTextMessageAsync(update.Message.Chat.Id, "Бот запущен, настройте список контактов") 
                    |> Async.AwaitTask
                ()

            if (currentState = Disabled) then ()

            elif (currentState <> Disabled && update.Message.Text = "/stop") then
                do! stateRepo.SetState senderId Disabled

            elif (currentState = Enabled && update.Message.Text = "/set_peers") then
                do! stateRepo.SetState senderId SetPeers
                let! _ = 
                    client.SendTextMessageAsync(
                        update.Message.Chat.Id, 
                        "Присылайте контакты пользователей, групп и переписок")
                    |> Async.AwaitTask
                ()

            elif (currentState = SetPeers && update.Message.Text = "/stop_set_peers") then
                do! stateRepo.SetState senderId Enabled
                let! _ = 
                    client.SendTextMessageAsync(update.Message.Chat.Id, 
                        "Все сообщения кроме команд будут пересланы указанным пользователям") 
                    |> Async.AwaitTask
                ()

            elif (currentState = Enabled && update.Message.Text = "/list_peers") then 
                do! handleListPeers client update

            elif (currentState = Enabled) then 
                do! handleUpdateToForward client update

            elif (currentState = SetPeers) then 
                do! handleNewPeer client update 

            else
                let! _ = 
                    client.SendTextMessageAsync(update.Message.Chat.Id, "Бот находится в неожиданном состоянии") 
                    |> Async.AwaitTask
                ()
            
        } |> Async.StartAsTask :> Task

    let handleUpdateFunc = Func<ITelegramBotClient, Update, CancellationToken, Task>(handleUpdate)
    let errorHandlerFunc = Func<ITelegramBotClient, Exception, CancellationToken, Task>(fun _ e _ -> 
        log.Error(e, "Polling error")
        Task.CompletedTask)
    
    let wait = task {
        do! botClient.SetMyCommandsAsync(seq {
            BotCommand(Command = "/start", Description = "Запускает бота")
            BotCommand(Command = "/set_peers", Description = "Переходит в режим сохранения контактов")
            BotCommand(Command = "/stop_set_peers", Description = "Завершает режим сохранения контактов")
            BotCommand(Command = "/list_peers", Description = "Отправляет сообщение со списком сохранённых контактов")
            BotCommand(Command = "/stop", Description = "Останавливает бота")

        })
        do (botClient.StartReceiving(handleUpdateFunc, errorHandlerFunc))
    
        let! me = botClient.GetMeAsync()
        Console.WriteLine($"Start listening for @{me.Username}")
        Console.ReadLine() |> ignore
    }

    wait