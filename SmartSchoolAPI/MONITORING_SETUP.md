# ربط مراقب SmartSchool باللوحة والتنبيهات الخارجية

يعتمد مراقب Windows على متغيرات البيئة حتى لا تُحفظ كلمات المرور أو رموز الوصول داخل السكربت أو Git. بعد تغيير المتغيرات، شغّل مهمة **SmartSchool API Monitor** يدويًا مرة واحدة من «Task Scheduler»، أو انتظر حتى تشغيلها الدوري التالي.

## ربط لوحة SmartSchool Pulse

بعد نشر لوحة الويب، اضبط متغيري البيئة للمستخدم الذي يشغّل مهمة المراقبة:

```powershell
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_DASHBOARD_INGEST_URL', 'https://YOUR-DASHBOARD-DOMAIN/api/monitoring/ingest', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_DASHBOARD_INGEST_TOKEN', 'YOUR-RANDOM-INGEST-TOKEN', 'User')
```

أنشئ الرمز نفسه في إعدادات اللوحة باسم `MONITOR_INGEST_TOKEN`. لا تستخدم رمزًا قصيرًا أو مشاركًا مع خدمة أخرى. بعد التهيئة، يرسل المراقب قراءة صحية واحدة كل دقيقة إلى اللوحة.

## تنبيهات Telegram

أنشئ بوتًا عبر `@BotFather`، ثم أرسل إليه رسالة، واستخرج `chat_id` من واجهة Telegram المناسبة. اضبط القيم على Windows فقط:

```powershell
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_TELEGRAM_BOT_TOKEN', 'BOT_TOKEN', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_TELEGRAM_CHAT_ID', 'CHAT_ID', 'User')
```

لا يرسل المراقب إلى Telegram إلا عند وجود تنبيه أو استعادة للخدمة، وليس لكل فحص ناجح.

## تنبيهات البريد الإلكتروني

اضبط متغيرات SMTP المناسبة لمزوّد البريد. مثال الأسماء المطلوبة:

```powershell
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_SMTP_HOST', 'smtp.example.com', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_SMTP_PORT', '587', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_SMTP_SSL', 'true', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_SMTP_USER', 'alerts@example.com', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_SMTP_PASSWORD', 'APP_PASSWORD', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_SMTP_FROM', 'alerts@example.com', 'User')
[Environment]::SetEnvironmentVariable('SMARTSCHOOL_SMTP_TO', 'operations@example.com', 'User')
```

استخدم كلمة مرور تطبيق إذا كان مزود البريد يدعمها، ولا تستخدم كلمة مرور الحساب الرئيسية.

## اختبار آمن

شغّل المراقب يدويًا بعد ضبط القيم:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\api-monitor.ps1
Get-Content .\api-monitor.log -Tail 10
```

تحقق من ظهور `FORWARD` لربط اللوحة أو `NOTIFY` للتنبيه الخارجي. ظهور `FORWARD_FAIL` أو `NOTIFY_FAIL` لا يوقف مراقبة API المحلية؛ بل يسجّل سبب الفشل في `api-monitor.log`.
