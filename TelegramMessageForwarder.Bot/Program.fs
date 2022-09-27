open Serilog
open Telegram.Bot
open Types
open Repositories

let log = 
    (new LoggerConfiguration())
        .WriteTo.Console()
        .WriteTo.File("log.txt")
        .CreateLogger()

let stateRepo = InMemoryStateRepository<int64>()
let peerRepo = InMemoryPeerRepository<int64, Peer>();

let client = Forwared.initTelegramClient(log)
client.Wait()

let bot = Bot.getBot log stateRepo peerRepo client.Result
bot.Wait()

