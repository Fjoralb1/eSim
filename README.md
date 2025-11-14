# ⚙️ Mobysim Backend

**Mobysim** is a modern **.NET 8 backend** designed for eSIM reselling and automation.  
It integrates multiple distributors, manages product pricing and resellers, handles PayPal payments, and exposes secure REST APIs for both public and partner-facing systems.

---

## 🧩 Overview

Mobysim acts as an **eSIM reseller hub**, sitting between end users, resellers, and eSIM distributors.  
It automatically retrieves available plans, applies markup logic, manages reseller pricing tiers, and captures PayPal payments — all while maintaining transactional integrity and logging.

---

## 🏗️ Architecture

| Layer | Description |
|-------|--------------|
| **eSim.Api** | ASP.NET Core 8 API — main entry point for external clients (public & reseller). |
| **eSim.Core** | Domain layer — contains models, business logic, and calculation rules. |
| **eSim.Infrastructure** | Handles data access, external API integrations (distributors, PayPal), and persistence. |
| **SQL Server** | Main relational database for product, order, and user data. |
| **Serilog** | Logging system with rolling file output and retention policy. |

---

## 🧠 Key Features

✅ Multi-distributor integration (e.g., eSIMAccess)  
✅ Automated PayPal order and capture  
✅ Multi-tier pricing system (retail vs. reseller)  
✅ Dynamic price updates from distributors  
✅ Background services for scheduled synchronization  
✅ PDF invoice generation (via PDFsharp or wkhtmltopdf)  
✅ Robust logging (file + console, daily rolling)  
✅ Environment-based configuration (Development, Production)  
✅ Fully container/deployment ready (.NET 8 compatible)

---

## 🧰 Technologies

| Component | Stack |
|------------|--------|
| **Framework** | ASP.NET Core 8.0 |
| **Language** | C# |
| **ORM** | Entity Framework Core |
| **Logging** | Serilog |
| **Payments** | PayPal REST API |
| **Integration** | Distributor APIs (JSON/REST) |
| **Database** | SQL Server / Azure SQL |
| **PDF Generation** | PDFsharp / wkhtmltopdf |
| **CI/CD** | GitHub Actions |

---

## 🧱 Project Structure

Mobysim/
├─ eSim.sln
├─ eSim.Api/ # Main API project
│ ├─ Controllers/
│ ├─ DTOs/
│ ├─ appsettings.json
│ └─ Program.cs
├─ eSim.Core/ # Business logic, entities, calculations
├─ eSim.Infrastructure/ # EF Core, integrations, data access
├─ .gitignore
└─ README.md

---

## 🧪 Development Setup

# Requirements
- .NET 8 SDK
- SQL Server
- PayPal developer credentials
- Distributor API credentials
  
# Run API Locally
cd eSim.Api
dotnet restore
dotnet build
dotnet run

# Apply Migrations
dotnet ef migrations add Init --project eSim.Infrastructure --startup-project eSim.API
dotnet ef database update --project eSim.Infrastructure --startup-project eSim.API

---

## 🧱 Logging Setup
Mobysim uses Serilog with rolling daily files.

# Log Folder (default)
/Logs

# Output format example
2025-01-10 17:22:13 [INFO] Captured PayPal order 93FJ221
2025-01-10 17:22:14 [ERROR] Distributor API timeout

---

## 🔌 Integrations

# PayPal
"PayPal": {
  "ClientId": "xxx",
  "ClientSecret": "xxx",
  "Environment": "Sandbox",
  "ReturnCurrency": "EUR"
}
# Distributor (example: eSIMAccess)
"EsimAccess": {
  "AccessCode": "",
  "OrderUrl": "/order",
  "PackagesUrl": "/list",
  "QueryUrl": "/query",
  "LocationsUrl": "/list",
  "DataUsage": "/query"
}

---

## 🔒 Security Setup

# Recommended Production Settings

ASPNETCORE_ENVIRONMENT=Production
Use HTTPS enforced
Store secrets in environment variables
Enable webhook verification
Allow CORS only for frontend domain

---

## 👨‍💻 Author

# Fjoralb Filej
# Backend Developer (.NET)
# 🌍 fioralbifilej@gmail.com
