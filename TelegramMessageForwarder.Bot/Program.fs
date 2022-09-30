open System
open System.Threading
open Serilog
open Telegram.Bot
open Types
open Repositories

let main = async {
    let log = 
        (new LoggerConfiguration())
            .WriteTo.Console()
            .CreateLogger()

    let stateRepo = InMemoryStateRepository<int64>()
    let peerRepo = InMemoryPeerRepository<int64, Peer>();

    use! client =  Forwarder.initTelegramClient(log) |> Async.AwaitTask

    let! bot = Bot.initBotClient log stateRepo peerRepo client |> Async.AwaitTask

    ignore bot

    use mre = new ManualResetEvent(false)

    let handleStop _ _ = 
        mre.Set() |> ignore

    ConsoleCancelEventHandler handleStop
    |> Console.CancelKeyPress.AddHandler

    mre.WaitOne(-1, false) |> ignore
    log.Information "Stopping..."
}

Async.RunSynchronously main
