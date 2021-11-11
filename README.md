# Usage

Bot for voting demos to be viewed at dfwc review.

Usage:
`!vote start <round>` - to start voting for round (only priveleged)
`!vote vq3 <player-nick>` or `!vote cpm <player-nick>` - to vote for player (everyone)
`!vote stop` - to stop voting and download demos (only priveleged)
`!vote stats vq3|cpm` - to list votes (only priveleged)

# Configuration

Configuration is loaded from `config.json` at run location.

```json
{
  "UseArchive": false, // download whole archive and repack or download demos individualy
  "ChannelName": "w00deh", // channel to attach
  "ExtractDirectory": "./data", // directory to save all demos. Demos'll be saved at subdirectories `round<N>/vq3` and `round<N>/cpm`
  "Superusers": ["w00deh"], // list of priveleged users (ones who can start/stop voting)
  "Vq3": {
    "RequiredPlayers": ["I V Y X 8", "/cdtu/xas.th", "DeX.ks.ua"], // players that's needed to be viewed
    "RequiredTop": 5, // top players that's needed to be viewed
    "TotalDemos": 10 // maximum amount of demos
  },
  "Cpm": {
    // same as `Vq3` section
    "RequiredPlayers": ["<KABCORP>", "esc?nebuLa", "w00dy.th"],
    "RequiredTop": 5,
    "TotalDemos": 10
  },
  "UserWeights": {
    // list of players' vote weights
    "w00deh": 0,
    "rantrave1001010": 100
  },
  "Credentials": {
    // nickname and secret to bot's twitch login
    "Nickname": "<bot-nickname>",
    "Password": "<bot-secret>" // meant to start with `oauth:`
  }
}
```
