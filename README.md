# AppTime Limiter

A Windows desktop app that limits how much time you spend in the apps you choose, by schedule and by a daily budget. When time is up, it closes the app.

> **Status: early development.** The design is settled, but v1 isn't built yet, so there's nothing to download or run. The roadmap below tracks progress.

## Why

Willpower is unreliable at 2 a.m. AppTime Limiter makes your limits automatic: you set the rules once, and it enforces them every time you sign in to Windows. It started as a personal tool for keeping gaming in check, but it's designed to work with any app.

## Planned features (v1)

- **Groups.** Put any number of apps (processes) into a named, colored group, such as "Games". Each app belongs to one group.
- **Weekly schedule grid.** Seven days in 30-minute cells. Mark each cell:
  - **Free**: usage is tracked, but doesn't count toward your budget.
  - **Limited**: usage is tracked and counts toward your daily budget.
  - **Blocked**: the app is closed right away, with a notification.
- **Shared daily budget.** One time allowance across all Limited groups. When it runs out, the app is closed.
- **Warnings.** Windows notifications before a limit ends (5 minutes by default, up to 3 per group, all adjustable).
- **Everything adjustable.** Schedules and limits live in a plain JSON settings file. Nothing is hardcoded.
- **Local only.** No accounts, no cloud, no telemetry. Your data stays on your machine.

## Tech stack

- C# on .NET, with [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/) for the UI
- [WPF-UI](https://github.com/lepoco/wpfui) for the modern Windows 11 look
- Windows only

## Roadmap

- [ ] Groups and process picker
- [ ] Weekly schedule grid
- [ ] Shared daily budget
- [ ] Warning notifications
- [ ] Close-on-block behavior
- [ ] Stats page
- [ ] Browse-for-exe option in the process picker
- [ ] Optional grace period when the budget runs out

## Limitations

AppTime Limiter is a self-control aid, not a security tool. It runs without admin rights, so anyone with access to your Windows account can stop or uninstall it. It works best when you want to hold yourself to a limit and need the friction.

Website blocking is out of scope for the app itself. A browser extension such as [LeechBlock NG](https://www.proginosko.com/leechblock/) is the likely companion for that.

## Building from source

Not available yet. Build instructions will be added once there is code.

## Contributing

Issues and ideas are welcome. The project is at an early stage and the design is still moving, so please open an issue to discuss a change before sending a pull request.

## License

[MIT](LICENSE)
