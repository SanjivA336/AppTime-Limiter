# AppTime Limiter

Set your limits once. Windows keeps you to them.

AppTime Limiter closes the apps you choose when you've hit your daily time or you're outside your allowed hours. It started as a way to keep my own gaming in check, but it works with any app.

> **Heads up:** this is v0. The core works, but there's no installer yet, so for now you build it yourself (it's two commands, see below).

## How it works

Put apps in a **group**, like "Games". Each group gets a weekly schedule made of 30-minute cells. Paint each cell one of three ways:

| Cell | What it means |
| --- | --- |
| ⬜ **Free** (grey) | Play away. Your time is tracked, but it's not counted against you. |
| 🟦 **Limited** | Allowed, but the minutes come out of your daily budget. |
| 🟥 **Blocked** | Not right now. The app is closed as soon as it's spotted. |

Every group has its own **daily budget** for each day of the week: a pot of minutes that only drains during that group's Limited time and refills at midnight. Time spent in one group never uses up another group's budget. When a group's pot is empty, its Limited apps get closed too.

That lets you do things like *3 hours on weekends between 6 and 11 PM, and 2 hours on weekdays between 8 and 10 PM*. Paint the windows, then set each day's budget in the box above its column. The window says *when* you can play, and the budget says *how long*.

You also get a heads-up before the axe falls, like *"You have 5 minutes left of Games."* Pick when (up to three warnings per group).

When an app has to go, it's asked nicely first so it can save. If it hasn't closed after 3 seconds, it's closed for it.

## What you get

- A card for each group, showing its color, its first few apps (with their icons), and where it stands right now: Limited with the time you have left, or Free or Blocked along with when that ends (like "Blocked until today at 6:00 PM"). If you've used up the day's budget, a Limited group shows as Blocked until the next Free time, or until the budget resets
- Groups with Limited time get a bar along the bottom of their card: green, blue or red for the state right now, shrinking as the day's budget is spent. It glows while one of the group's apps is running, and drifting stripes and a pulsing glow show when that app is using up your budget
- Click a card to edit that group, with its name, color, apps and warnings on one tab and its schedule on another. Nothing changes until you press **Save changes**: closing the window cancels, and **Revert** puts everything back without closing
- An optional lock for each group, from 5 seconds up to 5 minutes. A locked group opens to nothing but a countdown, and you can't see or change anything about it (including deleting it) until it reaches zero. Every time you open it, the wait starts over
- Each group has its own budget for each day of the week, set right above that day's column (it appears once the group has some Limited time)
- A paint-it-yourself weekly grid, plus "copy this day to…" (it copies the day's budget too) so you don't repeat yourself
- Warnings before time runs out
- A tray icon, so it keeps working after you close the window
- An option to start with Windows
- No admin rights, no account, no cloud, no tracking. Everything stays on your PC.
- Safety rails: it won't let you limit core Windows stuff like `explorer`, so you can't lock yourself out of your own desktop

## Try it

You'll need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or newer.

```powershell
git clone <this repo>
cd <this repo>
dotnet run --project src/AppTimeLimiter.App
```

To run the tests:

```powershell
dotnet test
```

Want a standalone build?

```powershell
dotnet publish src/AppTimeLimiter.App -c Release -r win-x64 --self-contained false -o publish
```

Working in VS Code? Press **F5**. The "dev data" profile keeps its settings in a throwaway `.dev-data` folder and never registers itself to start with Windows, so it can't mess with your real setup. Tasks for build, test and publish are in the Terminal > Run Task menu.

If you turn on *Start with Windows* and later move the `.exe`, just switch the setting off and on again so it learns the new location.

## Where your stuff lives

Everything is in `%LOCALAPPDATA%\AppTimeLimiter\`:

- `settings.json`: your groups, with each group's schedule, warnings, lock and daily budgets (older files with one shared budget are upgraded automatically: every group starts with a copy)
- `usage.json`: how long each group has been used, day by day
- `app.log`: a small log, handy if something looks off

Settings are plain text, so you can edit them by hand. Each day's schedule is 48 letters, one per half hour from midnight: `F` is Free, `L` is Limited, `B` is Blocked. Days are listed Sunday first. If the app can't read something, it plays it safe and treats it as Blocked.

If a file ever gets corrupted, the app saves a copy (`*.corrupt-<time>.json`) and starts fresh. It won't quietly throw your data away.

## Good to know

This is a self-control tool, not a security system.

- It runs as you, so anyone on your Windows account can quit it. It's built to give *you* a bit of friction, not to stop a determined you.
- It can't close an app that's running as administrator unless it's running as administrator too. If that happens, it'll tell you.
- The app picker shows what's running right now, so start the app first, then add it.
- It doesn't block websites. A browser extension like [LeechBlock NG](https://www.proginosko.com/leechblock/) pairs nicely with it.

## What's done, what's next

**Done**
- [x] Groups and the app picker
- [x] Weekly schedule grid
- [x] Daily budgets, set separately for each group and each day
- [x] Warning notifications
- [x] Closing apps when time's up
- [x] Tray icon and start with Windows

**Planned**
- [ ] Stats page: see where your time actually goes
- [ ] Browse for an `.exe` instead of only picking running apps
- [ ] A short grace period when the budget runs out
- [ ] A cooling-off delay when you *loosen* a rule (tightening stays instant)
- [ ] Website blocking through a browser extension
- [ ] An installer, so you don't need to build it yourself

Got an idea? Open an issue and say hi.

## Project layout

```
src/AppTimeLimiter.Core      The brains: schedule, budget, warnings, settings (no UI)
src/AppTimeLimiter.App       The Windows app: window, tray icon, notifications
tests/AppTimeLimiter.Tests   Tests for the brains
```

The decisions about what to close and when to warn live in `EnforcementEngine` and `LimitForecast`. They're told the time and the running apps rather than looking them up, which is why the tests can try any scenario without waiting around for the real clock.

## Patch notes

**v0**: First working version. Groups, weekly schedule, per-group daily budgets, warnings, locks, closing apps, tray icon.

## Contributing

Thanks for stopping by! This is early and the design is still shifting, so please open an issue before sending a pull request. And run `dotnet test` first.

## License

[MIT](LICENSE). Do what you like with it.
