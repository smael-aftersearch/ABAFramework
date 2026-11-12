## وضعیت فعلی قالب عمومی ABA

بر اساس [اجرای موفق پنج مسیر CI](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43298754432)، قالب عمومی .NET 10 دارای EF Core برای PostgreSQL و Microsoft SQL Server، Redis، Wolverine با تست تراکنشی Outbox و بازیابی پس از راه‌اندازی مجدد، تست Keycloak واقعی، تست جداسازی داده‌های Tenant در EF، Docker غیرریشه‌ای، NuGet Template و مهارت‌های AI است.

در پروژه تولیدشده، برای ساخت یک Context بدون دیتابیس دستور `python3 tools/new_context.py create --name Inventory` و برای Context دارای DbContext اختصاصی با پشتیبانی هر دو دیتابیس دستور `python3 tools/new_context.py create --name Inventory --with-db` را اجرا کنید. سپس `python3 tools/new_context.py check`، `dotnet build` و `dotnet test` را اجرا کنید. نام Inventory فقط مثال نام‌گذاری دستور است، نه ماژولی که فریم‌ورک به‌صورت پیش‌فرض ایجاد کند.

**محدودیت مهم:** ساخت خودکار Context به معنی ساخت قواعد واقعی کسب‌وکار نیست. برای Production هنوز امنیت مالکیت Tenant در Identity Provider، Row-Level Security دیتابیس، تست قطع ناگهانی/Dead-letter، مهاجرت و Rollback، تست فشار و استقرار امن لازم است.

---

## قابلیت‌های جدید فریم‌ورک عمومی

در قالب `aba-api`، انتخاب دیتابیس با `Platform__Database__Provider` بین `PostgreSql` و `SqlServer` انجام می‌شود. رشته اتصال از `ConnectionStrings__AbaDatabase` گرفته می‌شود. Redis با `Platform__Cache__Provider=Redis` و `ConnectionStrings__AbaRedis` فعال می‌شود؛ Wolverine نیز با `Platform__Messaging__Enabled=true` از همان دیتابیس انتخاب‌شده برای پیام‌های پایدار استفاده می‌کند. OpenTelemetry و کنترل Claim مربوط به Tenant به‌صورت اختیاری موجودند. این بخش‌ها در پروژه جدید بدون مثال Academy ایجاد می‌شوند.

[تست موفق PostgreSQL، SQL Server، Redis و Wolverine](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43290885904). تکمیل امنیت Tenant در سطح داده، احراز هویت واقعی IdP و تست خرابی/بازیابی Outbox هنوز انجام نشده است.

---

# شروع سریع پروژه عمومی API

برای ساخت یک پروژه جدید **بدون مثال Academy و بدون هیچ دامنه تجاری فرضی**، از ریشه مخزن اجرا کنید:

```bash
dotnet new install ./templates/aba-api
dotnet new aba-api -n MyCompany.MyApi -o ../MyCompany.MyApi
cd ../MyCompany.MyApi
dotnet run --project src/MyCompany.MyApi.Api
```

در محیط Development به `http://127.0.0.1:5174/scalar` بروید تا رابط گرافیکی و تعاملی تست API را ببینید. `/openapi/v1.json` سند OpenAPI تولیدشده و `/health/live` سلامت سرویس است. هر پروژه جدید فایل‌های `AGENTS.md`، `.agents/skills/aba-engineering/SKILL.md`، نقشه مستندات و تست xUnit دارد.

تست واقعی قالب: `bash scripts/verify-starter.sh`، با [.NET 10 CI موفق](https://github.com/smael-aftersearch/ABAFramework/actions/runs/43286946630). تست‌های واقعی PostgreSQL/Redis/Wolverine و استقرار Production هنوز کامل نشده‌اند.

---

## راهنمای نمونه قدیمی و ابزارهای توسعه

# راهنمای کوتاه فارسی: استفاده از ایجنت و Skill معماری ABA

این مخزن دو نوع راهنمای هوش مصنوعی دارد: `AGENTS.md` برای قواعد ثابت در سطح پروژه، و `.agents/skills/aba-engineering/SKILL.md` برای روند اجرای تسک. `docs/ai/INDEX.md` نقشه مستندات و `docs/ai/knowledge-map.json` فهرست ماشین‌خوان آنهاست. قوانین معماری فقط متنی نیستند؛ `scripts/governance.py` و `scripts/ai_check.py` در CI اجرا می‌شوند.

## به ایجنت چه بگویم؟

این جمله را در محیط Coding Agent که به همین مخزن دسترسی دارد بفرست:

> ابتدا AGENTS.md و Skill موجود در .agents/skills/aba-engineering/SKILL.md را بخوان. برای این تسک، با python3 scripts/ai_check.py brief --task "..." مستندات مرتبط را پیدا کن. پیش از کدنویسی مالک هر تصمیم را در ownership.yaml بررسی کن. سپس کد، تست و مستندات مربوط را با هم تغییر بده، چک‌های Governance را اجرا کن و گزارش اثر معماری و نتیجه تست‌ها را بده. از وابستگی مستقیم بین Domainهای مختلف و تغییر بی‌نسخه Contractها خودداری کن.

ابزار جدید `plan` مستندات و تست‌های پیشنهادی هر تسک را فهرست می‌کند؛ مالکیت را به‌صورت قطعی تعیین نمی‌کند. ابزار `verify` فرمان‌های بررسی ثابت را اجرا و نتیجه هرکدام را در JSON/Markdown ثبت می‌کند. در صورت نبود .NET خروجی `NOT_RUN` است، نه PASS. خروجی‌ها در `artifacts/verification/` ذخیره می‌شوند و CI نیز آن‌ها را نگه می‌دارد.

برای مثال:

```bash
python3 scripts/ai_check.py brief --task "اضافه کردن استرداد پرداخت و رویداد نسخه‌دار" --file samples/Academy/Payments.Contracts/PaymentContracts.cs
python3 scripts/task_protocol.py plan --task "اضافه کردن استرداد پرداخت و رویداد نسخه‌دار" --file samples/Academy/Payments.Contracts/PaymentContracts.cs
python3 scripts/test_task_protocol.py
python3 scripts/task_protocol.py verify --profile static --output artifacts/verification/static.json
# نیازمند .NET 10 برای بررسی کامل:
python3 scripts/task_protocol.py verify --profile all --output artifacts/verification/full.json
```

## نصب و رفتار واقعی

- در Codex، دستورهای دائمی پروژه در `AGENTS.md` و Skill محلی در `.agents/skills/` قرار دارد. در ابزارهای دیگر، قابلیت شناسایی خودکار ممکن است متفاوت باشد.
- برای استفاده مستقل از مخزن، فایل `skill.zip` جداگانه آماده شده است؛ آن را از بخش Skills در محیط پشتیبانی‌شده اضافه کنید.
- برای GitHub Copilot، مسیر `.github/copilot-instructions.md` و برای Claude Code و Gemini راهنماهای سطح ریشه نیز قرار داده شده‌اند.
- **مهم:** صرف وجود فایل Skill باعث نمی‌شود همه مدل‌ها آن را خودکار بخوانند. ایجنت باید به مخزن دسترسی داشته باشد و دستورهای پروژه در محیط مربوط فعال/لود شده باشند.
- Skill به‌صورت خودکار در پس‌زمینه تغییرات را رصد نمی‌کند؛ هنگام اجرای تسک مستندات را بررسی و تکمیل می‌کند. تست‌های CI برخی خطاهای ساختاری را می‌گیرند، اما جای بررسی معنایی را نمی‌گیرند.

## تست‌ها کجاست و ایجنت چطور باید تست بنویسد؟

فایل `docs/testing/TEST-STRATEGY.md` قانون اصلی تست پروژه و `tests/README.md` راهنمای یافتن تست‌هاست. در نسخه فعلی، `tests/Aba.SmokeTests/Program.cs` تست کنسولی C# است و پروژه xUnit نیست؛ چک‌های منفی معماری و مستندات در `scripts/test_governance.py` و `scripts/test_ai_check.py` قرار دارند.

در هر تسک جدید، قبل از کدنویسی از ایجنت بخواه: «یک سناریوی موفق، حداقل یک سناریوی خطا و invariant مربوط را مشخص کن؛ سپس تست و کد را با هم بنویس. در پایان فرمان‌های اجراشده و نتیجه دقیق PASS/FAIL/NOT RUN را گزارش بده». دستور کلی `bash scripts/verify-all.sh` شامل چک‌های Python و، در صورت نصب .NET 10، Build و smoke C# است. تست واقعی PostgreSQL/Redis/Wolverine، xUnit، امنیت و بار هنوز در نسخه اول پیاده نشده است.
