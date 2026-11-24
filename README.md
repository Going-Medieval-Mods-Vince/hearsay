# Hearsay

Talk with your villagers in *Going Medieval*. A BepInEx 5 mod: select a settler, press the round **Talk** button
next to their order buttons (bottom of the info panel), and chat.

Two ways to talk, both reading the settler's live situation from the game:

- **Written dialogue (no setup):** pick what to say from a list; the villager answers with written lines chosen
  from their mood, hunger, sleep, wounds, work they love or hate, perks, faith, friends and rivals, the season and
  the food stores. Now and then they ask *you* something back, and you pick your answer. No API key, nothing to set up.
- **AI replies (optional):** add a Gemini key and you can type freely; the same topics show as quick prompts.

Settings are in the game: the **HEARSAY** tab in the top bar, after Objectives. There you pick the reply mode (`Auto`: AI
when a key is set, written otherwise; `Written`; `AI`), paste and test your Gemini key, choose the model, thinking
level, memory and reply length, whether talking meets a villager's Social need (on by default: the same
game effect as a chat between two villagers, once per in-game day each), make the village ask your title again, and reload dialogue files. Everything is
saved to `BepInEx/config/hearsay.cfg`, which you can still edit by hand.

## What a settler knows (read from the game when you send a message)

Wherever the game has its own words for something, Hearsay uses them, so a villager describes their life the way
the game does.

- **The village:** name, season, time, day count, temperature, every villager, and a by-eye guess of how long the village's food
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
- Conditions: `mood<30`, `food<3` (days of food), `pop>=10`, `day`, `hour`, `temp`, `age`, `talks`;
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
- `BepInEx/config/hearsay.cfg`: chat mode, Gemini key, model (`gemini-3.8-flash`), thinking level, reply length.
  The key is only sent to Google's Gemini API.
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
