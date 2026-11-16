# The Wasteland Remembers (Living Wasteland) on Meitou: play-test guide

For your home PC, starting from nothing (Meitou not installed yet). About **1 hour**: 20 minutes of setup, 30 to 40 minutes of play.

Every step says what you should see. **If something looks different, stop and send me a screenshot.** Nothing here touches your Kenshi
saves, and Part 4 puts normal Kenshi back with one double-click.

**What Meitou is:** a free, open-source replacement engine for Kenshi. It runs your own copy of Kenshi's files, so Kenshi must be
installed, but it does not change Kenshi itself.

**You need:** Kenshi on Steam, Python (already on that PC from earlier sessions), and a reasonably recent graphics card (yours is fine).

**Played before? Skip to Part 2b** (updating takes two minutes; setup is already done).

---

## Part 1. Setup (once)

### Step 1. Get our files

1. Download the zip I sent you: **`TheWastelandRemembers_build_20261114l.zip`**.
   (No zip handy? On GitHub open your **kenshi** repository, switch the branch button from `main` to
   **`claude/ecstatic-albattani-rys5jx`**, then green **Code** button > **Download ZIP**. Same thing.)
2. Right-click the zip > **Extract All...** > Extract. Put it somewhere easy, like your Desktop.
3. Open the extracted folder, then the **`modgen`** folder inside it. Every file this guide mentions is in `modgen`.

### Step 2. Switch your mods to the Meitou setup

1. **Close Kenshi** if it is running.
2. In `modgen`, double-click **`switch_to_meitou.bat`**. A black window opens.
3. It saves your current Kenshi mod setup (into `C:\Users\<you>\LW_switch_backup`), then builds and installs the Meitou version of our
   mods. This takes a minute or two. Lots of text scrolls by; that is normal.
4. ✅ You should see: **`Done: you are on the MEITOU setup.`** Press any key to close it.
   ❌ If you see `ERROR`, `INVALID` or `Something went wrong`: screenshot the window and send it to me. Your Kenshi setup is already
   saved.

(If Windows says "Windows protected your PC": click **More info** > **Run anyway**. If your Kenshi is not in the usual Steam folder,
tell me and I will tell you the one line to change.)

### Step 3. Install the Meitou launcher

1. Download the launcher:
   **https://github.com/VlcekM/MeitouClient/releases/latest/download/MeitouLauncher-win-x64-Setup.exe**
2. Run it. Windows will probably warn that the publisher is unknown (the project is not code-signed): click **More info** > **Run
   anyway**.
3. The launcher opens a first-start window:
   * **Install folder:** leave as it is.
   * **Kenshi folder:** it should find your Steam Kenshi by itself. If it does not, click **Browse** and pick your Kenshi folder (the one
     with a `data` folder inside).
   * **Renderer:** leave on **Auto**.
   * **Launch as Kenshi on Steam** (if it asks): leave it on.

### Step 4. Pick the game version

1. Next to the **Play** button there is a version list. Pick **0.19.0** (or the newest release) and let it download.
2. Leave **Nightly builds** **unticked**.
3. Our mods are checked on 0.17.3 up to 0.19.0. Faces in the Atlas and squad roles need **0.19** or later. If a newer version than
   0.19 is out, tell me before playing on it: I run a quick check first (it takes me a few minutes).

### Step 5. Tick the right mods

Open the launcher's **Mods** list. **Careful:** the launcher ticks every mod it has never seen before, so some will be ticked that
should not be. Make it match this:

| Mod | Ticked? |
|---|---|
| **LivingWasteland** | ✅ Yes |
| **LivingWastelandCannibals** | ✅ Yes |
| **LivingWastelandOutlaws** | ✅ Yes |
| **LivingWastelandHeat** | ✅ Yes |
| **LivingWastelandMeitou** (our code helper) | ✅ Yes |
| LivingWastelandTest (only if left from an older install) | ❌ No (it is retired; you can delete its folder) |
| SquadRecord (only if you see it) | ❌ No |
| KenshiLua, BFrizz's Extra Extensions, CheatMenu, LivingWastelandLua, anything RE_Kenshi | ❌ No (Meitou cannot run these) |
| Your other data mods (Nice Map and so on) | Up to you. For the cleanest test, untick them. |

Our five should sit **below** any other mods you keep. If the order looks wrong, press **Arrange**.
Optional: save this as a preset called `LW Meitou test`.

### Step 6. Start

1. Press **Play**.
2. If it asks whether to **trust** the code of **LivingWastelandMeitou**: say **Yes**.
3. The first start can take **several minutes** (Meitou builds caches). Later starts are quicker.
4. ❌ If the game does not start: the launcher's status line says why. Screenshot it and send it to me.

---

## Part 2. The test, step by step (about an hour; stop whenever you like)

Start a **new game** with the **Drifter's Last** start (an old save works too, but your record would start from zero mid-game). Go down
the list; for each step write **yes**, **no**, or what you saw instead. Screenshots help most where it says so. **Stop when you run out
of time:** the earlier steps matter most.

**Keys and helpers:** **F10** = Meitou's debug menu (teleport, spawn, money, heal; our rows are at the bottom). **Insert** = console
(`money 5000`, `heal`, `help`). **F5** quick save, **F9** quick load, **Space** pause. The speed buttons (top of the screen) make
waiting quicker. **U** opens our Squad record window.

**Before you start:** copy your Meitou saves folder somewhere safe if you care about old saves (Meitou test runs write autosaves).

### A. The first ten minutes (in Drifter's Last)

1. **It loads.** The game starts and you are in Drifter's Last.
2. **People talk.** Walk around for a minute. Speech bubbles over townsfolk?
3. **Trade works.** Talk to the barman and a shopkeeper: the trade window opens as normal?
4. **The window.** Press **U**. The **Squad record** window opens (press U again: it closes). Also look top right, a little below the
   top: a **Squad record** button does the same. **Screenshot the window.**
5. **The tabs.** Click through **Record**, **Titles**, **History**, **Fallen**, **Atlas**, **Settings**. Each shows something (History and
   Fallen may say "nothing yet").
6. **Settings search.** In **Settings**, click the search box and type `bounty`: only bounty settings stay. Click **X** to clear.
7. **Settings help.** Hover the mouse over any setting: a box under it explains it. (If no box appears, click the setting's name
   instead and tell me the hover did not work.)
8. **Bar talk.** Stand inside the bar for a minute or two. Patrons say things to each other every half minute or so?

### B. Out of town: a first fight

9. **Lookouts.** Use **F10** to spawn a hostile group (Dust Bandits or Hungry Bandits) about 200 m away, or walk toward one. Before the
   fight starts, one of your people should call them out ("Dust Bandits on the ridge!") and another answer.
10. **The fight.** Fight them. Afterwards, did someone say something about the win?
11. **Skill notices.** During or after fights, small gold messages should float up at the top ("Name   Toughness 6") when someone
    gains a skill level. **Screenshot one.**
12. **The record.** Press **U**: did kills and knockouts go up for the right people? Anything new in **History**?

### C. In a town of a faction (teleport with F10 to Stack, Heng or Blister Hill)

13. **Town criers.** Stay in the town a while (speed it up). Now and then a local should call out "Hear the news!" and some news, or a
    faction proclamation. About every six minutes, not more. Does it feel right, too much, too little?
14. **Alms.** Talk to a handful of townsfolk (not shopkeepers). About one in seven asks for alms (Empire) or an offering for Okran
    (Holy Nation). Give **100 cats**: the cats go, a green **"+1"** with the faction's name should float up.
15. **A job.** Talk to townsfolk until someone offers work. Accept. **F10 > "Wasteland Remembers: quests"** should list it.
16. **Gossip.** Listen to townsfolk for a few minutes: rumours, outlaw news or war talk? (It is random; "no" is fine, just say so.)
17. **Heat.** Knock out one or two of the town's own people (F5 first!). The guards should react, and the Squad record shows heat for
    the town at the bottom. Then F9 to undo it.

### D. Saving

18. **Save and load.** Press **F5**, then **F9**. Press **U**: are your numbers, History and Atlas still there?

### E. If you still have time (any order)

19. **A loss.** (F5 first.) Let a squadmate die. Over the body, the squad should mourn them **by name**; the **Fallen** tab shows them
    with an epitaph. For three days the squad's lines turn grim and use their name ("That one was for Beep."). F9 afterwards if
    you like.
20. **Titles fast.** In **F10**, spawn **Holy Lord Phoenix** and kill him: the killer becomes **Narko** (Titles tab). Holy Nation people
    should be wary of them afterwards.
21. **Contracts.** Take an outlaw contract from a bounty giver (Empire towns). When you report the kill or hand in the body, a green
    "+4" or "+5" should float up for that faction.
22. **The Atlas.** After a few bar visits, open **Atlas**: lines you overheard should be listed under places and people. Click a
    faction: how it stands with you, its wars, how it sees your titles (Holy Nation and Empire: also their title for you once you have
    done contracts for them, or their insult for you if they are hostile). **Screenshot a person page and a faction page.**
23. **Neck slot.** Open a character's inventory (**I**): an extra empty square (the neck slot). Screenshot where it sits.
24. **Record only.** In Settings, click **Record only**: our talk stops, the record keeps counting. Then **Defaults** to undo.
25. **Atlas faces (0.19).** Open the Atlas and click a person listed by name (not one marked "?", who is still only a rumour):
    their face should show at the top right. **Screenshot it.**
26. **Squad roles (0.19).** Recruit a second squad or split yours (two squads). Open **U > Squads**. Click the role button of one squad
    (a list opens) and pick **Patrol nearby (defend only)**: it should start walking around by itself. Give it a move order: it obeys, then goes back
    to patrolling. Pick **Off (your orders only)**: it stops. If a role says "did not take", tell me which one. With a building of yours, stand the leader next to it and click
    **Make home**, then pick **Guard home**: the squad should stay there (and sleep in its beds at night).
    **Escort:** pick **Escort <your other squad>** from the list: the squad should follow your main squad around and
    fight for it. Save and load (F5, F9): it should keep escorting.
27. **With your other mods** (Recap, Execution, Brutality...): after the steps above, does Recap's "Previously on" include our moments
    (titles, last stands)? Do Execution's finishing blows count in the record?

## Part 2b. Second session (start here if you have played before)

### Update first (2 minutes)

1. Quit Meitou completely (the game, not just to the menu): a running game keeps the old mods until it is restarted.
2. Extract **`TheWastelandRemembers_build_20261114l.zip`** into a **new, empty folder** (so no older build's files are left in it).
3. In that new folder's `modgen` folder, double-click **`install_meitou.bat`**. It ends with "Done" and "no quest-breaking problems".
   Last session ran older data than the build sent, so this matters: the game's log now says which build is installed
   ("build 20261114l (data and helper match)"; a WARNING line there means the install did not take).
4. Launcher: version **0.19.0** (the 0.19.1 nightly is checked too, but nightlies are untested), our five mods ticked, below your others.
5. Load your save from last time, or start a new game (a new game shows the "arriving" fixes best).

### A. The fixes from last time (about 15 minutes, in Drifter's Last)

Each one is yes / no / what you saw instead.

1. **Arriving:** no wall of speech bubbles; our lines start a little after you arrive, then one every few seconds.
2. **Talk amount:** U > Settings > "Added talk amount" says 40% (if it still says 100%, your old setting was kept: set 40 there).
   **Rumour and gossip amount** should say 40% too (an old 100% is kept: set 40, or press Defaults).
3. **Settings tab:** Record only and Defaults sit on the search row, not on top of the tabs.
4. **Neck slot:** open an inventory (I). The extra square is under the weight panel, above Inventory, not on Open Bag.
5. **Record page:** U > Record: a gold header per person with titles, then labelled lines (Combat, Travel...), no rows of zeros.
6. **Days:** History and the Atlas say the same day as the game clock (bottom right).
7. **The Atlas:** only outlaws whose ground is near Drifter's Last should turn up now, and a person only after you hear of them twice.
8. **The crier:** stay in Drifter's Last a while (speed it up): about every six minutes a local calls something out.
9. **How it reads:** does the talk sound like Kenshi now? Screenshot any line that feels off or "AI".

### B. What the first session never reached (the rest of your time, any order)

10. **A faction town.** F10 teleport to **Stack** (Holy Nation) or **Heng** (Empire) and stay ten minutes:
    * a crier calling out real news, or a proclamation;
    * one in seven townsfolk asking for **alms / an offering**: give 100 cats and a green "+1" for that faction should float up;
    * if you hold a title, people reacting to it.
11. **A town fight** (F5 first): attack a few guards. The faction's standing should show as **one** red notice a few seconds after things
    calm down (say "United Cities  -17"), not one per blow. Skill notices: one per skill once it stops climbing. F9 to undo.
12. **Two squads.** Split your squad in two (squad window). In **U > Squads**, the role button opens a list:
    * **Patrol nearby (defend only)**: the squad walks around by itself; a move order takes over, then it goes back to patrolling;
    * **Escort <your other squad>**: it follows your other squad and fights for it. **F5, F9**: it should still be escorting;
    * a role that does not start says "did not take": tell me which;
    * near a building you built: **Make home**, then **Guard home**.
13. **Atlas faces and titles:** open a person you know by name (their face, top right); open a faction you have met (Empire and Holy Nation
    give their own title for you once you have done contracts for them, or an insult if they hate you).
14. **A contract** (Empire towns, bounty givers): report a kill or hand in a body: a green "+4" / "+5" for that faction.
15. **With your other mods** (optional, last): tick Recap, Execution, Brutality; load your save: does Recap's "Previously on" include our
    moments? Do finishing blows and severed limbs count in the record?

### Afterwards

Run **`save_logs_meitou.bat`** (it now finds Meitou 0.19's log by itself) and send me the zip, your yes/no list (with the numbers above)
and screenshots: a notice, the Squads list open, an Atlas face, anything that looked wrong.

---

## Part 3. Send me the logs

1. Quit Meitou.
2. In `modgen`, double-click **`save_logs_meitou.bat`**.
3. ✅ It says **`Saved: ...\Desktop\LW_meitou_logs_<date>.zip`**. Send me that zip, your yes/no list from Part 2 (with the step numbers), and your
   screenshots (the Squad record window especially).

---

## Part 4. Back to normal Kenshi

1. Close Meitou.
2. In `modgen`, double-click **`switch_to_kenshi.bat`**. ✅ It ends with **`Done: you are on the KENSHI setup.`**
   This puts back your Kenshi setup **exactly as it was** before Step 2.
3. **To also get today's Kenshi fixes** (named outlaws now actually spawn): double-click **`install_all_with_lua.bat`** in `modgen`,
   then start Kenshi from Steam and check your mod list as usual.

You can switch back and forth any time. Do not delete the `LW_switch_backup` folder in your user folder.

---

## If something goes wrong

* **The launcher cannot find Kenshi:** in the launcher's Settings, point it at the Kenshi folder that has `data` inside.
* **The game will not start:** compare the mod list with Step 5 (an extra or missing mod is the usual cause), then send me the
  launcher's status line and the logs (Part 3).
* **It crashes:** note what you were doing in the last minute, run `save_logs_meitou.bat`, send me the zip.
* **Something of ours seems off** (a job that does not pay, a window that looks wrong, no Squad record button): a screenshot and the
  logs are all I need.
* **Did our helper load at all?** The log (on Meitou 0.19: `log.txt` inside `%LocalAppData%\Meitou\versions\<version>`; paste `%LocalAppData%\Meitou` into File Explorer's
  address bar) has lines starting with **`LivingWastelandMeitou`**. If there are none, the helper did not load: tell me.
