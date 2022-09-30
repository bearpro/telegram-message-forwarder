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

    let! bot = Bot.getBot log stateRepo peerRepo client |> Async.AwaitTask

    ignore bot

    use mre = new ManualResetEventSlim()

    let handleStop _ _ = 
        log.Information "Stopping..."
        mre.Set()

    ConsoleCancelEventHandler handleStop
    |> Console.CancelKeyPress.AddHandler

    mre.Wait()
}

Async.RunSynchronously main
