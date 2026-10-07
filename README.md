# MailReaper Community Edition ⚡

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-blue.svg)](https://microsoft.com)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](https://github.com/pigy75/MailReaper-Community/pulls)

**MailReaper** is a high-performance Windows desktop email backup, recovery, and forensic export engine built on **.NET 9** and **WPF**.

It allows individual users, system administrators, and IT engineers to extract, index, and convert email mailboxes from **Gmail (Google OAuth 2.0)** and **IMAP / Microsoft 365** into standard, portable **Outlook PST** archives—**without needing Microsoft Outlook installed on the machine**.

---

## 🌟 Key Capabilities

- **Direct-to-PST Engine**: Generates valid Microsoft Outlook `.pst` files natively without third-party COM interop or Outlook desktop dependencies.
- **Modern Authentication**: Built-in Google OAuth 2.0 flow (browser loopback) and secure IMAP credentials management.
- **Embedded SQLite Storage**: Employs SQLite WAL mode (`Write-Ahead Logging`) and page cache tuning for multi-threaded indexing, handling hundreds of thousands of messages effortlessly.
- **Offline Message Viewer**: Inspect downloaded emails, attachments, headers, and metadata in a responsive forensic UI before exporting.
- **Incremental Resumption**: Interrupted backups can be resumed seamlessly with token caching and message deduplication.

---

## 📊 Feature Comparison: Community vs. PRO

MailReaper follows an **Open Core** model. The Community Edition is 100% open source under the permissive **MIT License**, designed for individual developers and personal backups.

| Feature | Community Edition (Free / Open Source) | MailReaper PRO & Enterprise |
| :--- | :---: | :---: |
| **License** | MIT (Open Source) | Commercial Perpetual |
| **Gmail OAuth 2.0 Backup** | ✅ Yes | ✅ Yes |
| **IMAP & Standard Mailboxes** | ✅ Yes | ✅ Yes |
| **Embedded SQLite Local Cache** | ✅ Yes | ✅ Yes |
| **Offline Forensic Viewer** | ✅ Yes | ✅ Yes |
| **PST Export Capacity** | **Up to 5 GB** | **🚀 Unlimited (200+ GB verified)** |
| **Automated PST Multi-part Splitter** | — | **✅ Yes (40 GB chunks for Outlook safety)** |
| **Google Workspace Domain-Wide Backup** | — | **✅ Yes (Service Account JSON & Multi-tenant)** |
| **Industrial Resiliency & Auto-Quarantine** | Standard | **✅ High-throughput crash resilience** |
| **Dedicated Enterprise Support** | GitHub Discussions | **✅ Priority Email & Remote Support** |

> 🌐 **Need domain-wide backup or unlimited PST export?**  
> Visit the official portal to acquire a PRO or Enterprise license:  
> 👉 **[https://mailreaper.peer2peer.cloud](https://mailreaper.peer2peer.cloud)**

---

## 🛠️ Tech Stack & Architecture

```
MailReaper-Community/
├── src/
│   ├── GmailToPst.Core/        # Core interfaces, models, and domain abstractions
│   ├── GmailToPst.Providers/   # Google OAuth, Gmail API, and IMAP providers
│   ├── GmailToPst.Storage/     # SQLite local indexing & WAL cache repository
│   ├── GmailToPst.Exporters/   # Standalone PST creation engine
│   └── GmailToPst.UI/          # Modern Windows desktop WPF interface (MVVM)
└── tests/
    └── GmailToPst.Tests/       # Unit and integration test suite
```

- **Runtime**: [.NET 9.0](https://dotnet.microsoft.com/download/dotnet/9.0) (C# 13)
- **UI Framework**: Windows Presentation Foundation (WPF) with modern MVVM design
- **Protocols**: Google APIs Client Library, MailKit / MimeKit
- **Database**: System.Data.SQLite / Microsoft.Data.Sqlite

---

## 🚀 Building from Source

### Prerequisites
- Windows 10 / 11 (64-bit)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Visual Studio 2022 (v17.12+) or JetBrains Rider with .NET desktop workload

### Steps

1. **Clone the repository:**
   ```powershell
   git clone https://github.com/pigy75/MailReaper-Community.git
   cd MailReaper-Community
   ```

2. **Restore and Build:**
   ```powershell
   dotnet restore MailReaper.sln
   dotnet build MailReaper.sln -c Release
   ```

3. **Run the Application:**
   ```powershell
   dotnet run --project src/GmailToPst.UI -c Release
   ```

---

## 🤝 Contributing

Contributions to the Community Edition are warmly welcome! Whether it is fixing a bug, improving documentation, or adding support for new IMAP quirks:

1. Fork the repo (`https://github.com/pigy75/MailReaper-Community/fork`)
2. Create your feature branch (`git checkout -b feature/awesome-improvement`)
3. Commit your changes (`git commit -m 'Add awesome improvement'`)
4. Push to the branch (`git push origin feature/awesome-improvement`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

For enterprise licensing and high-volume deployment inquiries, visit [mailreaper.peer2peer.cloud](https://mailreaper.peer2peer.cloud).
