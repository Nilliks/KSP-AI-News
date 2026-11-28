# Kerbal News Network

An AI-powered press corps for **Kerbal Space Program 1.12**. Every launch, landing, docking and disaster in your save gets reported, about one Kerbin day later, by seven in-universe outlets. Each article has its own comment section, and the public argues about it on Kerbnet, the in-game social network. The size of the reaction matches the size of the event. An uncrewed test hop gets a two-paragraph brief. A first arrival at Duna makes the front page and gets expert commentary and a statement from the Planetary Council. A crew loss is covered somberly and stays in the public's memory.

All news lives inside your save file. Reverting, quickloading or crashing rolls back what the public knows, exactly like the rest of the game.

## Features (v1)

- **News from real save data:** vessel names, part counts, mass, height, stages, delta-v, engines, crew, bodies and situations.
- **Seven outlets:** Kerbin Daily Herald, Orbital Observer, The Kerbal Inquirer, Mun Monthly, KSC Watch, Kerbin Science Review (scientific community) and the Planetary Council Press Office (official statements, biggest news only).
- **Comments** from a cast of recurring commenters, and a **Kerbnet** social feed with a diverse mix of voices.
- **Reaction scaled to significance:** novelty fades with repeats, danger beats success, firsts and distant bodies matter more.
- **Public mood:** excitement, trust and concerns that carry from mission to mission. A failure is reported through the memory of the last similar one.
- **Quiet-week features and opinion pieces** when nothing has happened for a few days (rate-limited).
- **Agency profile and lore journal:** the press treats them as canon.
- **Silliness slider** from grounded (0) to full Kerbal (10).
- **Universe tracker:** satellites, stations, bases, crew in space and debris per body over time.
- **Every major AI provider:** DeepSeek (default), Claude, ChatGPT, Gemini, Grok, Mistral, OpenRouter, Ollama, LM Studio and any OpenAI-compatible endpoint. **Offline templates** need no key at all.
- **Context compression:** older news and lore are folded into a running archive summary, so prompts stay short in long careers.
- **Spell check while typing:** misspelled words in the browser's text boxes get a red wavy underline; right-click one for suggestions, *Add to dictionary* or *Ignore*. Fully offline (no AI calls), and it knows the game's planets, parts, kerbals, craft, outlets and installed mods. Turn it off under Settings.
- **Model comparison:** each article records which model wrote it, and *Rewrite with current model* regenerates a story so you can compare models.

## Install

1. Copy `GameData/KerbalNews` into your KSP `GameData` folder (or build it yourself, below).
2. Start KSP and load a save. Click the newspaper button on the toolbar.
3. Open **Settings**, pick a provider, paste your API key, and click **Test connection**. Untick *Offline mode*.

Your API key is stored only in `GameData/KerbalNews/PluginData/secrets.cfg`. It is never written to your save, and it is git-ignored.

## Cost

The mod makes one AI call per story, covering all of that story's outlets, comments and social posts. Small events go to the cheap model, and routine events often get no coverage at all. There are no calls during time warp, and only one quiet-day feature per Kerbin day. Archive compression uses the cheap model and runs rarely.

## Build

**Windows** (no .NET SDK needed, only Visual Studio Build Tools):

```powershell
.\build.ps1 -Install                              # uses the default Steam path
.\build.ps1 -KspDir "D:\Games\KSP" -Install
```

**Linux/macOS** (Mono): `./build.sh` runs the unit tests and compile-checks the KSP layer against stubs. Set `KSP_DIR` to build the real DLL.

## Layout

- `src/Core`: game-independent engine (events, stories, significance, mood, prompts, providers, persistence, compression). Fully unit-tested.
- `src/KSP`: KSP glue (GameEvents hooks, craft and world snapshots, ScenarioModule, UnityWebRequest transport, IMGUI news site).
- `tests`: core tests, run with `mono`.
- `stubs`: minimal Unity/KSP API stubs used only for compile checks.

Uses only stock GameEvents and patches nothing, so it works alongside other mods.

**KSP Mining:** if the KSP Mining mod is installed, the press also covers it: the first ore brought home from another world, the first cargo of each ore, first drills and first cast bars, record hauls, big sales that move the KERBEX price, and weekly price swings. Small items ride along in the background news roundup and cost no AI call of their own. The link is read-only (it never changes funds or anything in KSP Mining) and does nothing when that mod is absent.

## Livestream announcer voice

The livestream announcer can speak through ElevenLabs (your own key, under Settings). Stock announcer lines recorded with "Ship these lines with the mod" go in `GameData/KerbalNews/Voice/<voice-model-style>/`. They play for every player, even without a key. Voice audio is generated with [ElevenLabs](https://elevenlabs.io).

## Spell check word list

The spell checker's English word list (`GameData/KerbalNews/Dictionary/en.txt`) is built from [SCOWL](http://wordlist.aspell.net/) (Spell Checker Oriented Word Lists, size 60, American and British spellings) by Kevin Atkinson and contributors; its copyright and permission notice is in `GameData/KerbalNews/Dictionary/SCOWL-Copyright.txt`. KSP words live in `kerbal.txt` next to it, and words you add in game go to `PluginData/spelling-words.txt`.
