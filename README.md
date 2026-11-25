# Hearsay

Talk with your villagers in *Going Medieval*. A BepInEx 5 mod: select a settler, press the round **Talk** button
next to their order buttons (bottom of the info panel), and chat.

Two ways to talk, both reading the settler's live situation from the game:

- **Written dialogue (no setup):** pick what to say from a list; the villager answers with written lines chosen
  from their mood, hunger, sleep, wounds, work they love or hate, perks, faith, friends and rivals, the season and
  the food stores. Now and then they ask *you* something back, and you pick your answer. No API key, nothing to set up.
- **AI replies (optional):** connect any OpenAI-compatible AI service and you can type freely; the same topics show
  as quick prompts. Gemini (`https://generativelanguage.googleapis.com/v1beta/openai`), OpenRouter, OpenAI, DeepSeek,
  Groq, Mistral, or a model on your own computer with Ollama or LM Studio (no key, no cost).

Settings are in the game: the **HEARSAY** tab in the top bar, after Objectives. There you pick the reply mode (`Auto`: AI
when a key is set, written otherwise; `Written`; `AI`), set the server, paste and test your key, choose the model, thinking
level, memory and reply length, whether talking meets a villager's Social need (on by default: the same
game effect as a chat between two villagers, once per in-game day each), make the village ask your title again, and reload dialogue files. Everything is
saved to `BepInEx/config/hearsay.cfg`, which you can still edit by hand.

## What a settler knows (read from the game when you send a message)

Wherever the game has its own words for something, Hearsay uses them, so a villager describes their life the way
the game does.

- **The village:** name, season, time, day count, temperature, every villager, what it has become (camp,
  hamlet, village, market town, great stronghold: from the number of villagers and the game's own wealth figure plus your livestock and pets,
  which shapes how much respect villagers show), and a by-eye guess of how long the village's food
  would last, counting every food pile on the map except what's marked off-limits (each villager's own hunger rate from the game, about 89 food a day for most).
- **Themselves:** age, birthplace, former occupation and upbringing, pseudonym, perks (names and in-game
  descriptions), where they stand on the game's faith scale, their village duty.
- **Work:** skills with level and maximum, plus the game's interest word (Passionate, Eager, Unwilling...).
- **Right now:** the game's own activity line (what the settler list shows), wounds (tended or not), mood.
- **What is affecting them:** every active effect by its game name (hunger, sleep, weather, rooms, food,
  events...) with its mood value from the game data, strongest first.
- **The others:** friend, rival or neutral by the game's own thresholds, both ways, and their latest
  interactions with each villager from the game's social log.
- **History:** their last 10 life events and your conversation so far.

## Written dialogue

The default lines live in `dialogue/dialogue.json` (built into the DLL): 13 topics, 18 questions villagers ask,
about 450 lines. To add your own, drop any `.json` file with the same shape into
`BepInEx/config/Hearsay/dialogue/`: topics with an existing `id` get your lines added, new ids add new topics,
questions with an existing `id` are replaced.

- A line: `{"when": ["mood<30", "@hungry"], "say": ["variant one", "variant two"], "p": 2}`. The most specific
  line that applies wins (number of conditions + `p`); lines said recently are avoided.
- Conditions: `mood<30`, `food<3` (days of food), `pop>=10`, `standing>=3` (0 camp to 4 stronghold), `wealth`, `day`, `hour`, `temp`, `age`, `talks`;
  `fx:Hungry|VeryHungry|Slept*` (the game's effect ids), `perk:Gourmet`, `bg:Farmer` (background), `season:winter`,
  `faith:pagan|christian`, `best:Culinary`, `loves`, `hates`, `passion`, `wounded`, `untended`, `friend`, `rival`,
  `role`, `title`, `titleis:Lord`, `alone`, `woman`, `man`, `night`, `chance:30`, `@group` (named in `groups`), `!` to negate.
  About another villager: `o.friend`, `o.rival`, `o.neutral`, `o.likesme`, `o.hatesme`, `o.wounded`, `o.mood<25`,
  `o.perk:X`, `o.samefaith`...
- Placeholders: `{name}`, `{title}`, `{village}`, `{town}`, `{season}`, `{food}`, `{pop}`, `{day}`, `{worst}`/`{best}`
  (strongest bad/good effect by the game's name), `{wound}`, `{role}`, `{background}`, `{faithlevel}`, `{god}`,
  `{bestskill}`, `{loved}`, `{hated}`, `{friend}`, `{rival}`, `{other}`, `{o.he}`, `{o.him}`, `{o.his}`.
  Capitalise the first letter (`{Title}`) to capitalise the value. A line whose placeholder has no value is skipped.

## How the villagers address you

Nobody knows at first. The first villager you talk to asks what to call you (Lord, Lady, Chief, Captain, friend,
or type your own), and from then on everyone in that village uses it. Ask anyone to call you something else and it
changes for all. As the village grows, villagers may propose Baron or Baroness, and at 20 souls, Your Majesty.
It's stored per village.

## Built from the game alone

Everything here is written for Hearsay, using only Going Medieval's own code and data: the window frame is a
clone of one of the game's popups, the Talk button a copy of the panel's own round order buttons with the game's conversation icon, the text field is
built from Unity parts in the game's font.

## Files

- `BepInEx/plugins/Hearsay/Hearsay.dll`: the mod.
- `BepInEx/config/Hearsay/dialogue/*.json`: optional extra written dialogue.
- `BepInEx/config/hearsay.cfg`: chat mode, `[AI]` server address, key, model, thinking level, temperature, reply length.
  The key is only sent to the server address you set.
- `BepInEx/config/Hearsay/<village folder>.json`: per-village title and conversations.

## Build

```
cd path/to/hearsay
dotnet build -c Release -p:Deploy=true     # copies the DLL into the game's BepInEx/plugins/Hearsay
```

Set `GameDir` if the game isn't in the default Steam folder: `-p:GameDir="/path/to/Going Medieval"`.

## Roadmap

1. **Chat window, rich context, title** (this version).
2. **Standing:** each villager's trust and respect for you, changed by kind talks, kept or broken promises,
   overwork, hunger and deaths. It shapes their tone and whether they accept orders. Title upgrades proposed as
   the village grows.
3. **Hearsay:** news and rumours spread between villagers through their in-game conversations.
4. Villagers voicing their own chats, and a village chronicle.

## Token use and caching

Everything that stays the same between messages (the rules, who the villager is, the conversation so far) is sent
first, and the live state of the village (time, food, animals, mood, wounds) last. Gemini, OpenAI and most OpenRouter models cache
the repeated start automatically and charge less for it; the log shows how many tokens came from the cache on each message.
Animals are listed briefly, with details only for the ones that need attention.

## Villagers ask for a word

When something is on a villager's mind, a notice appears in the game's message stack ("Osmund would like a word",
or "... has something to tell you" for good news). Click it to talk: they speak first. Close it to let it pass;
unanswered requests lapse after a day. Hearsay looks once per in-game hour during the day, raises at most one at a
time, a few per day (`AsksPerDay`), never before the village has settled what to call you, and never the same
reason from the same villager twice within its cooldown. Nothing is sent to an AI until you click.

Worries: an untended wound, the food running out, grief, a suffering animal (only villagers who care about animals),
low spirits, loneliness, cold, hated work, wanting a room. Good news: the village growing into a hamlet, village,
town or stronghold, a raid beaten off, a good feast or service, a newcomer settling in, everyone in good spirits,
full stores before winter, a pet, a fine room, a good dream, a day of loved work, a merry drink, plain thanks.

Reasons live in the dialogue data under `"wants"` (`id`, `when`, `good`, `note` for the AI, `say` for written mode,
optional `ask`, `chance` per hour, `cooldown` in days, `p` priority) and can be added in your own dialogue files.
Extra conditions: `minmood`, `avgmood`, `caresanimals`, `animaltrouble`, `haspet`, `standingup`, `snubbed`, `snubbedgood`; placeholders
`{animal}`, `{pet}`, `{standing}`.

Answering matters (`RequestMoods`, on by default): come within 3 in-game hours and they feel *Heard out* (+3 mood
for 12 hours). Let a worry lapse or close its notice and they feel *Brushed off* (−4 for a day); ignored good news
costs no mood. Either way they remember it for up to three days: the next time you talk, the AI knows you never
came (or waved them off) and they may bring it up or be a bit cool; written mode greets you accordingly.
