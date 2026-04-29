# Saqqara Backend (Beauty.Api) — Claude Code Context

## Project Overview
ASP.NET Core Web API serving the Saqqara beauty platform. Handles auth, gifting, livestreams, battles, bookings, payments, and admin operations.

**Solution:** Saqqara.sln  
**Project:** Beauty.Api.csproj  
**Database:** MySQL on Azure (production password: SaqqaraGoLive_2026!)  
**Hosting:** Azure App Service

## Team
Kenny Stephen (owner/product) + Claude (sole developers).

## Stack
- **Framework:** ASP.NET Core (C#)
- **ORM:** Entity Framework Core + MySQL
- **Auth:** JWT bearer tokens
- **Payments:** Worldpay via CyberSource (Authvia gateway) + Stripe
- **Storage:** Azure Blob Storage
- **Email/Comms:** Power Automate webhooks, ACS (Azure Communication Services)
- **Real-time:** ACS Streaming Service (`AcsStreamingService.cs`)

## Key Files
```
Program.cs                  Startup, DI registration, EF seeding
Controllers/                One controller per domain area
  AdminController.cs        Dashboard stats, gift catalog, moderation
  GiftsController.cs        Send gifts, catalog, slab purchases
  StreamsController.cs      Livestream create/join/end
  BattlesController.cs      Battle arena matchmaking + resolution
  WalletController.cs       Slab balance, top-up, payouts
  AuthController.cs         Login, register, JWT refresh
  PaymentsController.cs     Worldpay/Stripe payment flows
Services/
  GiftBroadcastService.cs   Real-time gift events to stream viewers
  BattleMatchmakingService.cs  Auto-matching + BattleAutoResolveService
  WorldpayService.cs        Payment processing
  StripeService.cs          Stripe integration
  ContractGeneratorService.cs  Legal doc generation
Data/                       EF DbContext + entity configs
Domain/                     Domain models
Migrations/                 EF migrations
```

## Business Logic
- **Slabs:** virtual currency. 80 slabs = $1.50 ($0.01875/slab)
- **Gift revenue split:** Artist 25% / Saqqara 75% of gift slab value
- **Battle bonus:** 100% of battle gift value goes to Saqqara
- **Diamond Ring:** 10,000 slabs — top gift, triggers special frontend animation
- **EIN:** 46-3485577

## Gift Catalog Seeding
The startup seeder in `Program.cs` uses an additive pattern:
```csharp
if (!await db.GiftCatalog.AnyAsync()) { /* seed all */ }
else if (!await db.GiftCatalog.AnyAsync(g => g.SlabCost == 10000)) { /* add Diamond Ring only */ }
```
Always use `else if` checks for new gifts so existing databases get updated without re-seeding.

## Database
- **Production password:** `SaqqaraGoLive_2026!`  
  (Previous password `OWNER$2891aa` was retired — the `$` caused Azure App Service config issues)
- Connection string is in `appsettings.Production.json` and Azure App Service environment variables

## Payment Processing
- **Primary gateway:** Worldpay via CyberSource (Authvia) — Capital One partnership pricing
- **Fallback/secondary:** Stripe
- Pricing terms with Capital One/Worldpay not finalized until meeting scheduled for 2026-04-23

## Conventions
- No comments unless the WHY is non-obvious
- Controllers are thin — business logic belongs in Services
- EF migrations: always `dotnet ef migrations add <Name>` then review before applying
- Never hardcode secrets — use `appsettings.json` + Azure Key Vault / env vars

## Commands
```bash
dotnet run                          # start API (default port 5000/5001)
dotnet ef migrations add <Name>     # create migration
dotnet ef database update           # apply migrations
dotnet publish -c Release           # production build
```
