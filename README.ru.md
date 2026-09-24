<div align="center">

<a href="https://oblodai.com">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-white.svg">
    <img src="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-black.svg" alt="oblodai" height="52">
  </picture>
</a>

<h3>Официальный .NET / C# SDK платёжного шлюза <a href="https://oblodai.com">oblodai</a></h3>

Платежи, выплаты, платёжные ссылки, сплиты, статические кошельки, вебхуки — один API-ключ.

<img src="https://img.shields.io/badge/nuget-Oblodai%202.0.0-004880?style=flat-square" alt="nuget">
<img src="https://img.shields.io/badge/.NET-8%20%7C%2010-512BD4?style=flat-square" alt=".NET 8 | 10">
<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-000000?style=flat-square" alt="License: MIT"></a>

[Документация](https://docs.oblodai.com) · [Личный кабинет](https://my.oblodai.com) · [Read in English →](README.md)

</div>

---

Официальный .NET / C# SDK платёжного шлюза **Oblodai**: приём платежей, выплаты, пакетные операции,
платёжные ссылки, ссылки на выплату (крипто-чеки), сплиты, статические кошельки, переводы,
документы, вебхуки. Подпись запросов, типизированные модели и ошибки, идемпотентность и повторы — из
коробки. .NET 8 или 10 и только базовая библиотека (`HttpClient` + `System.Text.Json`): **ни одной
сторонней зависимости**. У каждой операции OpenAPI-контракта шлюза здесь есть метод, сгенерированный
из `openapi.json` генератором самого шлюза — ничего, что описывает API, не написано руками.

> **Базовый URL.** По умолчанию `https://api.oblodai.com`. При необходимости задайте `BaseUrl` и свои
> ключи при инициализации. Схема — `https://`; простой `http://` принимается только для loopback
> (`http://127.0.0.1:8095`) или с явным разрешением (`AllowInsecureBaseUrl = true` либо
> `OBLODAI_ALLOW_INSECURE=1`).

## Установка

```bash
dotnet add package Oblodai --version 2.0.0
```

.NET 8 или новее (в пакете сборки `net8.0` и `net10.0`). Всё, что нужно вызывающему коду, — в
пространстве имён `Oblodai`: клиент, модели запросов и ответов, словари (`PaymentStatus`,
`ErrorCode`, …) и ошибки; классы ресурсов — в `Oblodai.Resources`. Проверке вебхуков
(`WebhookVerifier`) не нужны ни клиент, ни API-ключ. Переходите с 1.3? Читайте
[MIGRATION-2.0.md](MIGRATION-2.0.md).

## Где взять ключи

У мерчанта **один API-ключ**, он выдаётся в [кабинете](https://my.oblodai.com) → **API-ключи**:
публичный id `oblodai_<hex>` и секрет `oblodai_live_<hex>`. Он подписывает все маршруты, которым
нужна подпись, — счета, выплаты, возвраты, ссылки, сплиты, кошельки, настройки, документы. Выбирать
на каждый вызов нечего. Песочная пара (`test_oblodai_<hex>` / `oblodai_test_<hex>`) работает с
копией шлюза без блокчейна и тестовыми деньгами.

```csharp
using Oblodai;

var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = publicId, Secret = secret });
```

Из окружения берутся `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET`, так что в настроенном контейнере хватит
`new OblodaiClient()`. Один клиент на ключ; он потокобезопасен — используйте его совместно.

## Быстрый старт

Каждый метод — `client.Resource.MethodAsync(…)`: запрос **именованными аргументами** (или моделью
запроса), затем `RequestOptions? options = null` и `CancellationToken cancellationToken = default`.
Создать счёт:

```csharp
var invoice = await oblodai.Payments.CreateAsync(
    amount: 25m,                // decimal, never double; sent as the string "25"
    currency: "USDT",           // what you price in: a fiat (USD, EUR, …) or a crypto asset
    network: "tron",            // omit to let the payer choose the network on the pay page
    orderId: "order-1001",      // your reference; the invoice is idempotent per order_id
    urlCallback: "https://shop.example/oblodai/webhook");

Console.WriteLine($"{invoice.Url} {invoice.Address} {invoice.Status}"); // status: created
```

Отправить деньги тем же ключом:

```csharp
var payout = await oblodai.Payouts.CreateAsync(
    address: "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
    amount: 10m,
    currency: "USDT",
    orderId: "payout-1",        // your reference; the payout is idempotent per order_id
    network: "tron",
    options: new RequestOptions { IdempotencyKey = "payout-1" });

Console.WriteLine($"{payout.Uuid} {payout.Status}"); // pending → … → confirmed
```

Запрос можно передать и моделью — каждый тип запроса это record с полями провода в `PascalCase`,
`required` там, где контракт их требует, — или собрать из словаря с именами провода:

```csharp
var request = new PaymentRequest { Amount = 25m, Currency = "USD", ToCurrency = "USDT" };
var priced = await oblodai.Payments.CreateAsync(request);

// Or from wire names, e.g. configuration; a double amount is refused (sdk.float_amount).
var fromConfig = Model.From<PaymentRequest>(new Dictionary<string, object?>
{
    ["amount"] = "25.00",
    ["currency"] = "EUR",
});
```

Готовые программы лежат в [`examples/`](examples): `AcceptPayment`, `Payout`, `Sandbox`,
`WebhookReceiver` — тесты прогоняют каждую против подменённого шлюза.

## Песочница / тестирование

Песочный ключ работает с копией шлюза без блокчейна: тестовый баланс из крана, имитация депозитов,
настоящие вебхуки. Бизнес-методы ведут себя ровно как в бою — меняется только ключ, а боевой ключ на
песочном маршруте отклоняется.

```csharp
await sandbox.Sandbox.FaucetAsync(amount: 1000m, asset: "USDT");

var invoice = await sandbox.Payments.CreateAsync(25m, "USDT", network: "tron", orderId: "sandbox-1");

// No amount pays exactly what is due; repeating a txid adds confirmations instead of paying twice.
var deposit = await sandbox.Sandbox.SimulateDepositAsync(invoiceId: invoice.Uuid);

Console.WriteLine($"{deposit.Txid} {deposit.Confirmations}");
```

- `Sandbox.FaucetAsync` начисляет тестовые деньги; `RequestOptions.IdempotencyKey` — это его
  собственное поле `idempotency_key`, так что повтор не пополнит дважды.
- `Sandbox.SimulateDepositAsync` оплачивает счёт: без `amount` — ровно сумму к оплате, иначе —
  недоплату или переплату; `confirmations` меньше требуемых проверяет переход pending → confirmed.
- `Sandbox.ListWebhooksAsync` показывает доставки с телами, `Sandbox.ReplayWebhookAsync(deliveryId)`
  отправляет доставку заново.
- `Webhooks.SendTestPaymentAsync` (и `…Payout`, `…Wallet`, `…Conversion`) репетирует доставку на
  любой приёмник: она подписана как настоящее событие и несёт `test: true`.
- `Sandbox.ResetAsync` отменяет открытые счета магазина и обнуляет его балансы.

## Обзор методов

16 ресурсов, 120 операций — вся мерчантская поверхность контракта. Имена зафиксированы в
[`names.lock`](names.lock): перегенерация, которая переименует или уберёт метод, падает как ломающее
изменение.

| Ресурс | Методы (`…Async`) | Операций |
| --- | --- | --- |
| `Payments` | Cancel · Create · GetAmlLinks · GetCheckoutConfig · GetInfo · GetQr · ListHistory · ListServices · Resolve · SendEmail · SetCheckoutConfig | 11 |
| `PaymentLinks` | Create · Get · List · Toggle | 4 |
| `Refunds` | BlockedWallet · Payment | 2 |
| `Payouts` | Approve · Calculate · Cancel · Create · CreateMass · CreateTransferBatch · GetInfo · ListHistory · ListServices · TransferToPersonal · TransferToUser · Validate | 12 |
| `PayoutLinks` | Cancel · ClaimPayout · Create · CreateBatch · Get · GetPayoutClaim · List | 7 |
| `Batches` | CreatePayment · CreatePayout · CreateRefund · GetInfo · Wait | 4 |
| `Splits` | CreateRule · DeleteRule · GetConfig · GetRecipientOptIn · ListRules · SetConfig · SetRecipientOptIn | 7 |
| `Wallets` | Block · Create · GetQr | 3 |
| `Account` | GetBalance · GetSummary · ListExchangeRates | 3 |
| `Webhooks` | ListDeliveries · Register · RequeueDelivery · ResendPayment · RotateSecret · SendLegacyTest · SendTestConversion · SendTestPayment · SendTestPayout · SendTestWallet · SetActive | 11 |
| `Settings` | ConfigureVrcs · DeleteAutoWithdrawRule · GetAccuracy · GetAutoConvert · GetAutoRefund · GetPaymentFeeConfig · GetPayoutFeeConfig · GetRefundFeeConfig · ListAcceptedCurrencies · ListApiLog · ListAutoWithdrawRules · ListDiscounts · SetAcceptedCurrencies · SetAccuracy · SetAutoConvert · SetAutoRefund · SetAutoWithdrawRule · SetDiscount · SetPaymentFeeConfig · SetPayoutFeeConfig · SetRefundFeeConfig | 21 |
| `ApiAllowlist` | AddEntry · List · RemoveEntry · SetEnabled | 4 |
| `Referrals` | GetInfo | 1 |
| `Documents` | CreateJob · DownloadJobFile · GetBalance · GetBatch · GetFees · GetJob · GetLedger · GetPaymentLink · GetPayoutLinkCheque · GetReferrals · GetSigned · GetSplit · GetStatement · GetWalletStatement · Wait · Download | 14 |
| `Checkout` | Get · GetOnramp · GetPublicPaymentLink · GetQr · GetSourceOfFundsForm · ListCurrencies · PaymentLink · SelectMethod · StartOnramp · SubmitSourceOfFunds | 10 |
| `Sandbox` | Faucet · ListWebhooks · OnboardStore · ReplayWebhook · Reset · SimulateDeposit | 6 |

`Checkout` — сторона плательщика (без ключа). Маршруты документов отвечают вне JSON-конверта и
возвращают `FileResult { Bytes, ContentType, Filename }` с `WriteToAsync(path)`. Отмена
`CancellationToken` бросает `OperationCanceledException`, а не ошибку SDK.

### Списки

Метод-список возвращает `PagePromise<T>`, который ещё ничего не запросил: `await` — одна страница,
`await foreach` — все элементы по всем страницам, `ByPageAsync()` — страница за страницей,
`AllAsync(max)` — собрать в список.

```csharp
var firstPage = await oblodai.Payments.ListHistoryAsync(limit: 50);  // one request: Items + Paginate

await foreach (var payment in oblodai.Payments.ListHistoryAsync(status: "paid"))
{
    Console.WriteLine($"{payment.OrderId} {payment.Amount}");       // every page, fetched lazily
}

await foreach (var page in oblodai.Payouts.ListHistoryAsync(limit: 100).ByPageAsync())
{
    Console.WriteLine($"{page.Items.Count} of {page.Paginate.Total}");
}
```

### Долгие операции

Пакеты и задания на документы принимаются сразу, а завершаются позже. `WaitAsync` опрашивает
операцию до конечного статуса и возвращает последний ответ — задание `failed` возвращается, а не
бросается, и разбирается как завершённое; ожидание, которое вышло за срок (10 минут по умолчанию), —
`sdk.wait_timeout`.

```csharp
var submitted = await oblodai.Batches.CreatePayoutAsync(payouts, onError: BatchOnError.Continue);
var batch = await oblodai.Batches.WaitAsync(submitted);            // polls until completed/stopped
Console.WriteLine($"{batch.Status}: {batch.Succeeded} ok, {batch.Failed} failed");

var job = await oblodai.Documents.CreateJobAsync(DocumentJobKind.Statement, from: "2026-01-01", to: "2026-06-30");
var ready = await oblodai.Documents.WaitAsync(job);                // done, failed or expired
if (ready.Status == DocumentJobStatus.Done)
{
    var file = await oblodai.Documents.DownloadAsync(ready);
    await file.WriteToAsync(Path.Combine(Path.GetTempPath(), file.Filename ?? "statement.pdf"));
}
```

### Статусы, словари, деньги

- Платёж: `select → created → confirm_check → paid | paid_over | wrong_amount | expired | cancelled`;
  `Statuses.IsPaymentPaid(status)` истинно для `paid`/`paid_over`. Выплата: `pending → approved →
  awaiting_cosign → broadcasting → sent → confirmed | failed | cancelled`.
- Словари (`PaymentStatus`, `ErrorCode`, `BatchStatus`, …) — `readonly record struct` поверх строки:
  сравнивайте с константами (`PaymentStatus.Paid`); значение новее этого SDK всё равно разбирается —
  `status.IsKnown` ложно, а `status.Value` его хранит. Незнакомое SDK поле попадает в `Extra` модели.
- Деньги — `decimal` в моделях и десятичная строка на проводе (`25.10m` ↔ `"25.10"`, масштаб
  сохраняется). `double` там, где ждут деньги, не компилируется; в теле-словаре он отклоняется с
  `sdk.float_amount` до отправки. `Money.Of`, `Money.Parse` и `Money.Format` преобразуют;
  `Money.Add`, `Money.Compare`, … работают со строками произвольной точности для сумм шире `decimal`.

## Вебхуки

`Webhooks.RegisterAsync(url)` задаёт (или заменяет) эндпоинт и возвращает секрет подписи — он
показывается один раз, сохраните его там, где его прочтёт приёмник. Проверке не нужны ни клиент, ни
API-ключ:

```csharp
var delivery = WebhookVerifier.VerifyDelivery(
    rawBody,                                        // the raw request bytes, not a re-serialized parse
    headers,
    new WebhookVerifyOptions { Secret = endpointSecret });

if (delivery.IsTest)
{
    return true;                                    // a rehearsal: signed, but no money moved
}

switch (delivery.Event)
{
    case PaymentWebhook payment when Statuses.IsPaymentPaid(payment.Status):
        Console.WriteLine($"paid: {payment.OrderId} {payment.PaymentAmount} {payment.PayerCurrency}");
        break;
    case PayoutWebhook payout:
        Console.WriteLine($"payout {payout.Uuid}: {payout.Status}");
        break;
}
```

Проверяйте **сырые** байты — пересериализованный разбор не совпадёт. MAC проверяется **до** времени
(окно по умолчанию 300 с, `ToleranceSeconds = 0` его отключает), поэтому окно не прощупать без
подписи. Событие — сгенерированная модель своего семейства: `PaymentWebhook`, `PayoutWebhook`,
`WalletWebhook`, `ConversionWebhook`, — или `UnknownWebhookEvent` для семейства, добавленного позже
(`WebhookVerifier.IsKnownEvent`). Отвечайте 4xx **только** на `SignatureException`; доставка,
прошедшая проверку, но нечитаемая, — `WebhookPayloadException` (`webhook.bad_payload`): отвечайте
5xx, шлюз повторит. Дедуплицируйте по `delivery.EventId` (`X-Webhook-Event-Id`, постоянен для
состояния), отбрасывайте доставки не по порядку через `WebhookVerifier.IsStale(delivery.Event,
lastSequence)`, а после `Webhooks.RotateSecretAsync` держите старый секрет в `PreviousSecret` не
меньше 26 часов.

## Ошибки

Любой сбой — `OblodaiException`. Его `Message` выглядит как `[код] текст (request_id=…)`, так что
одной строки лога хватает, чтобы найти вызов у нас; ветвитесь по `Code` — стабильной строке
`семейство.причина`, в коде `ErrorCode.*.Value`, — а не по тексту (`Description` — текст без кода).

```csharp
try
{
    await oblodai.Payouts.CreateAsync("TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx", 999999m, "USDT", "payout-2");
}
catch (ConflictException error) when (error.Code == ErrorCode.PayoutInsufficientFunds.Value)
{
    Console.WriteLine(error.Message);   // [payout.insufficient_funds] … (request_id=…)
    return error.RequestId;             // quote it to support
}
```

| Исключение | HTTP | Когда |
| --- | --- | --- |
| `ValidationException` | 400 | кривой запрос или бизнес-правило; `Field` называет поле |
| `AuthenticationException` | 401 | неверная подпись, неизвестный ключ, расхождение часов, IP не в списке |
| `PermissionException` | 403 | ключ верный, но сюда нельзя |
| `NotFoundException` | 404 | у мерчанта нет такого объекта |
| `ConflictException` / `IdempotencyConflictException` | 409 | конфликт состояния / тот же ключ, другое тело |
| `RateLimitException` | 429 | превышен лимит; задан `RetryAfter` |
| `UnavailableException` / `InternalException` | 503 / 5xx | отказал шлюз или его зависимость |
| `TransportException` | — | ответа нет: DNS, TCP, TLS, таймаут (`transport.*`), или вышел срок ожидания |
| `ConfigException` | — | отказ до отправки (`sdk.*`: опции, ключи, `sdk.float_amount`) |
| `ContractException` / `WebhookPayloadException` | — | ответ (или проверенная доставка) не той формы, что в документации |
| `SignatureException` | — | проверка вебхука не прошла |

Поля: `Code`, `Description`, `HttpStatus`, `Retryable` (решающий — SDK уже повторил, что следовало),
`RetryAfter`, `RequestId`, `Field`, `Synthetic` (ответил прокси).

## Опции вызова, сырой ответ и хуки

```csharp
var info = await oblodai.Payments.GetInfoAsync(
    orderId: "order-1001",
    options: new RequestOptions
    {
        Timeout = TimeSpan.FromSeconds(5),          // per attempt
        MaxRetries = 0,                             // this call only
        RequestId = "checkout-7f3a",                // X-Request-ID, to find it in our logs
        ExtraHeaders = new Dictionary<string, string> { ["X-Shop"] = "eu-1" },
    });

var raw = await oblodai.WithRawResponseAsync(c => c.Account.GetBalanceAsync());
Console.WriteLine($"{raw.Status} {raw.RequestId} {raw.Value.Balance}");

using var patient = oblodai.WithOptions(o => o with { Deadline = TimeSpan.FromMinutes(5) });
```

- `RequestOptions`: `IdempotencyKey` (генерируется сам на маршрутах, где шлюз дедуплицирует,
  одинаков на всех повторах, отклоняется с `sdk.idempotency_unsupported` там, где ничего не значит),
  `Timeout` (на попытку), `MaxRetries`, `ExtraHeaders`, `RequestId` (`X-Request-ID`; свой для каждого
  вызова, один на все его попытки).
- `WithRawResponseAsync` возвращает значение вместе со `Status`, `Headers` и `RequestId` (шлюза,
  иначе отправленный); для списка — первую страницу. Статус ошибки по-прежнему бросает исключение.
- `WithOptions` возвращает клиент с изменёнными опциями; пул соединений общий.

Хуки видят каждую попытку — для метрик, трассировки и логов; подпись в том, что им передаётся,
скрыта:

```csharp
var observed = new OblodaiClient(new OblodaiOptions
{
    PublicId = publicId,
    Secret = secret,
    Hooks = new Hooks
    {
        OnRequest = r => Console.WriteLine($"→ {r.OperationId} #{r.Attempt} {r.RequestId}"),
        OnResponse = r => Console.WriteLine($"← {r.Status} in {r.Elapsed.TotalMilliseconds} ms"),
    },
}, http);
```

Повторы: ошибка повторяется, только если API ответил `retryable: true`; ответ без конверта (прокси
502/503) и сбой транспорта повторяются только на безопасных маршрутах (`x-retry-safe` в контракте) и
на записях с ключом идемпотентности. `Retry-After` важнее вычисленной паузы; весь вызов ограничен
`Deadline`. Расхождение часов исправляется по заголовку `Date` после похожего на него 401; редиректы
не выполняются; тела ограничены (8 МиБ JSON, 64 МиБ документы).

## Настройка

| Опция | Что делает |
| --- | --- |
| `PublicId` / `Secret` | пара API-ключа мерчанта; подписывает все подписываемые маршруты |
| `BaseUrl` | адрес API; префикс пути сохраняется |
| `AllowInsecureBaseUrl` | разрешить простой `http://` для не-loopback хоста |
| `AdminToken` | токен онбординга своего шлюза (только `Sandbox.OnboardStoreAsync`) |
| `Timeout` | таймаут попытки (по умолчанию 30 с) |
| `Deadline` | бюджет одного вызова с повторами и паузами (по умолчанию 90 с) |
| `Retry` | политика повторов; `new RetryOptions { MaxRetries = 0 }` их отключает |
| `Hooks` | колбэки `OnRequest` / `OnResponse` на каждую попытку |
| `Logger` | структурный логгер диагностики SDK |
| `Headers` | заголовки на каждый запрос (зарезервированные имена игнорируются) |
| `Clock` | часы подписи; подменяются в тестах |
| `TimeProvider` | источник времени для пауз, дедлайнов и ожиданий; подменяется в тестах |

| Переменная окружения | Значение |
| --- | --- |
| `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET` | API-ключ |
| `OBLODAI_ADMIN_TOKEN` | токен онбординга своего шлюза |
| `OBLODAI_BASE_URL` | адрес API (по умолчанию `https://api.oblodai.com`) |
| `OBLODAI_LOG` | `debug` \| `info` \| `warn` \| `error` — включает консольный логгер |
| `OBLODAI_ALLOW_INSECURE` | `1` разрешает простой `http://` |

Явные опции важнее окружения; половина пары ключа отклоняется с `sdk.bad_config`. Секреты не
печатаются: опции скрывают секрет ключа и токен онбординга, а модель печатает поля с «секретными»
именами (`secret`, `token`, `passcode`, `claim_url`, …) как `[redacted]` — в самом свойстве значение
есть. Клиент принимает внешний `HttpClient`, так что подходит для `IHttpClientFactory`:

```csharp
services.AddHttpClient("oblodai")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
    .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
services.AddSingleton(provider => new OblodaiClient(
    new OblodaiOptions(), // OBLODAI_PUBLIC_ID / OBLODAI_SECRET from the environment
    provider.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));
```

## Сгенерированный код

`src/Oblodai/Generated/*.g.cs` — маршруты, словари, модели и методы ресурсов — генерируется из
`services/core/api/openapi.json` шлюза генератором `tools/sdkgen` в репозитории бэкенда (там `make
sdk` перегенерирует все восемь SDK) и руками не правится. Runtime вокруг него (транспорт, подпись,
повторы, ошибки, постраничность, вебхуки, ожидатели) написан руками и стабилен. `make ci` падает,
если сгенерированные файлы не совпадают с тем, что генератор делает из контракта.

## Разработка

```bash
make ci      # дрейф, сборка (-warnaserror), dotnet format, тесты, conformance, упаковка
make test    # только тесты
make live    # живой уровень против запущенного шлюза: OBLODAI_LIVE_URL=http://127.0.0.1:8095
```

Тулчейн .NET запускается в docker (`scripts/dotnet.sh`, SDK 10); проверке дрейфа нужны Go и
клон бэкенда (`OBLODAI_BACKEND`, по умолчанию `../oblodai-backend`), откуда берётся и общий набор
сценариев (`tools/sdkgen/conformance`): векторы подписи, повторы, деньги, совместимость вперёд.
[AGENTS.md](AGENTS.md) — та же поверхность на одной странице для агентов;
[CHANGELOG.md](CHANGELOG.md) — что изменилось; [MIGRATION-2.0.md](MIGRATION-2.0.md) — переход с 1.3.

## Лицензия

MIT — см. [LICENSE](LICENSE).
