module Types

type State = Disabled | Enabled | SetPeers

type Peer = Contact of Telegram.Bot.Types.Contact