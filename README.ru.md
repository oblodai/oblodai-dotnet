<div align="center">

<a href="https://oblodai.com">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-white.svg">
    <img src="https://raw.githubusercontent.com/oblodai/.github/main/brand/logo-black.svg" alt="oblodai" height="52">
  </picture>
</a>

<h3>Официальный .NET / C# SDK для платёжного шлюза <a href="https://oblodai.com">oblodai</a></h3>

Платежи, выплаты, платёжные ссылки, сплиты, статические кошельки, вебхуки — по одному API-ключу.

<img src="https://img.shields.io/badge/nuget-Oblodai%201.3.0-004880?style=flat-square" alt="nuget">
<a href="https://github.com/oblodai/oblodai-dotnet/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/oblodai/oblodai-dotnet/ci.yml?branch=main&style=flat-square&label=CI" alt="CI"></a>
<img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square" alt=".NET 8.0">
<a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-000000?style=flat-square" alt="License: MIT"></a>

[Documentation](https://docs.oblodai.com) · [Dashboard](https://my.oblodai.com) · [Read in English →](README.md)

</div>

---

Официальный .NET / C# SDK для платёжного шлюза **Oblodai**: приём платежей, выплаты, массовые
операции (батчи), платёжные ссылки, выплатные ссылки (крипточеки), сплиты, статические кошельки,
переводы, вебхуки. Подпись запросов, разбор ответов, типизированные ошибки, идемпотентность и
ретраи — из коробки. .NET 8+ и только базовая библиотека (`HttpClient` + `System.Text.Json`): **ноль
сторонних зависимостей** — и в рантайме, и в тестах; каждому маршруту шлюза здесь соответствует
метод, сгенерированный из снимка контракта самого шлюза и проверенный на эталонных ответах,
записанных с живого шлюза.

> **Base URL.** По умолчанию `https://api.oblodai.com`. При необходимости переопределите `BaseUrl` и
> передайте свои ключи при инициализации. Схема должна быть `https://`; обычный `http://`
> принимается только для loopback (`http://127.0.0.1:8095`) или с явным разрешением небезопасного
> адреса (`AllowInsecureBaseUrl = true` либо `OBLODAI_ALLOW_INSECURE=1`).

## Установка

```bash
dotnet add package Oblodai --version 1.3.0
```

Требуется .NET 8 или новее. Пакет называется `Oblodai`; клиент живёт в пространстве имён `Oblodai`,
сгенерированные записи запросов и справочники — в `Oblodai.Contract`, модели ответов — в
`Oblodai.Models`, ресурсные пространства — в `Oblodai.Resources`. Проверке вебхуков
(`WebhookVerifier`) не нужны ни клиент, ни API-ключ. Больше ничего не тянется: и SDK, и его тесты
ссылаются только на базовую библиотеку.

## Где взять ключи

У мерчанта **один API-ключ**, он выпускается в [личном кабинете](https://my.oblodai.com) →
**API keys**: public id `oblodai_<hex>` и секрет `oblodai_live_<hex>`. Им подписывается каждый
маршрут, требующий подписи: счета, выплаты, возвраты, ссылки, сплиты, кошельки,
настройки, документы. Выбирать на вызове нечего.

Пара песочницы (public id `test_oblodai_<hex>`, секрет `oblodai_test_<hex>`) приходит из
онбординга песочницы и работает с бесцепочечной копией шлюза; это тот же единственный
ключ, только над тестовыми деньгами.

```csharp
using Oblodai;

using var oblodai = new OblodaiClient(new OblodaiOptions { PublicId = publicId, Secret = secret });
```

Запасной вариант через окружение — `OBLODAI_PUBLIC_ID` / `OBLODAI_SECRET`, так что в настроенном
контейнере достаточно `new OblodaiClient()`. Заведение мерчантов (`Merchants.CreateAsync`,
`Merchants.CreateSandboxAsync`) идёт без подписи — self-hosted шлюз закрывает эти маршруты
**админ-токеном онбординга** (`AdminToken` или `OBLODAI_ADMIN_TOKEN`) в заголовке `X-Admin-Token`, и этот
токен нужен только для заведения.

> **Старые разделённые ключи.** У давно заведённых мерчантов ещё может оставаться старая
> пара `oblodai_pk_<hex>` / `oblodai_wk_<hex>`, где один ключ подписывал приём, а другой — вывод.
> Только в этом случае вызов может вернуть 403 `merchant.wrong_key_kind`; лечится выпуском
> актуального API-ключа в кабинете.

## Быстрый старт

Каждый метод — `…Async`, а последними параметрами принимает `RequestOptions? options = null,
CancellationToken cancellationToken = default`. Создаём счёт:

```csharp
using Oblodai;
using Oblodai.Contract;

using var oblodai = new OblodaiClient(); // OBLODAI_PUBLIC_ID / OBLODAI_SECRET from the environment

var invoice = await oblodai.Payments.CreateAsync(new PaymentRequest
{
    Amount = "25",              // amounts are decimal strings, never floats
    Currency = "USDT",          // what you price in: a fiat (USD, EUR, …) or a crypto asset
    Network = Network.Tron,     // omit to let the payer choose the network on the pay page
    OrderId = "order-1001",     // your reference; the invoice is idempotent per order_id
    UrlCallback = "https://shop.example/oblodai/webhook",
});

Console.WriteLine($"{invoice.Url} {invoice.Address} {invoice.Status}"); // status: created
```

Чтобы выставлять цену в фиате, задайте `Amount = "25", Currency = "USD", ToCurrency = "USDT"` —
`Currency` это то, что вы списываете, а `ToCurrency` — актив, который отправляет плательщик. Вывод
денег идёт тем же ключом:

```csharp
var payout = await oblodai.Payouts.CreateAsync(
    new PayoutRequest
    {
        Address = "TQrY8bkbpXKPt2LZbU8jqfnpFbUSF15sbx",
        Amount = "10",
        Currency = "USDT",
        Network = Network.Tron,
        OrderId = "payout-1",   // your reference; the payout is idempotent per order_id
    },
    new RequestOptions { IdempotencyKey = "payout-1" });

Console.WriteLine($"{payout.Uuid} {payout.Status}"); // pending → … → confirmed
```

Готовые к запуску программы лежат в [`examples/`](examples): `AcceptPayment`, `Payout`, `Sandbox`,
`WebhookReceiver`.

## Песочница и тестирование

Ключ песочницы работает с бесцепочечной копией шлюза: фейковый баланс из крана, симулированные
депозиты, настоящие вебхуки. Бизнес-эндпоинты ведут себя ровно так же, как в бою, — меняется только
ключ, а боевой ключ на маршруте песочницы отвергается.

```csharp
using Oblodai;
using Oblodai.Contract;

using var sandbox = new OblodaiClient(new OblodaiOptions { PublicId = testPublicId, Secret = testSecret });

await sandbox.Sandbox.FaucetAsync(new SandboxFaucetRequest { Asset = "USDT", Amount = "1000" });

var invoice = await sandbox.Payments.CreateAsync(new PaymentRequest
{
    Amount = "25", Currency = "USDT", Network = Network.Tron, OrderId = "sandbox-1",
});

// No Amount pays exactly what is due; repeating a Txid adds confirmations instead of paying twice.
var deposit = await sandbox.Sandbox.DepositAsync(new SandboxDepositRequest { InvoiceId = invoice.Uuid });

Console.WriteLine($"{deposit.Txid} {deposit.Confirmations}");
```

- `Sandbox.FaucetAsync` начисляет тестовые деньги. Передайте `IdempotencyKey`, если
  ретрай не должен пополнить баланс дважды.
- `Sandbox.DepositAsync` оплачивает счёт: без `Amount` платит ровно столько, сколько нужно, любое
  другое значение даёт недоплату или переплату, а `Confirmations` меньше требуемого прогоняет
  переход pending → confirmed. Повтор того же `Txid` добавляет подтверждения, а не платит второй раз.
- `Sandbox.WebhooksAsync` показывает доставки вместе с телами — то, что получил бы ваш приёмник, —
  а `Sandbox.ReplayAsync(deliveryId)` переотправляет терминальную доставку.
- `Webhooks.TestAsync(kind, request)` репетирует доставку на любой приёмник, хоть в песочнице, хоть в
  бою: она подписана в точности как настоящее событие и несёт `test: true` в подписанном теле (и
  `X-Webhook-Test: true`). Проверяйте `info.IsTest` и никогда не считайте такую доставку движением
  денег.
- `Sandbox.ResetAsync` отменяет открытые счета магазина и обнуляет его балансы.

## Обзор методов

16 пространств имён, 107 маршрутов — вся мерчантская поверхность.

| Пространство   | Методы                                                                                                                                                                                         | Маршрутов |
| -------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| `Payments`     | Create · Info/Get · Cancel · History/List · Batch · Qr · Services · SendEmail · Resend · PublicView · Select · PublicQr                                                                         | 12        |
| `Refunds`      | Create · Resolve · Batch                                                                                                                                                                        | 3         |
| `Payouts`      | Create · Validate · Calculate · Info/Get · Cancel · Approve · History/List · Mass · Batch · Services · Get/SetFeeConfig · Get/SetRefundFeeConfig                                                | 14        |
| `PayoutLinks`  | Create · Info/Get · List · Cancel · Batch · Cheque · ClaimPreview · Claim                                                                                                                       | 8         |
| `PaymentLinks` | Create · Info/Get · List · Toggle · PublicView · Checkout                                                                                                                                       | 6         |
| `Transfers`    | ToPersonal · ToUser · Batch                                                                                                                                                                     | 3         |
| `Batches`      | Info (прогресс асинхронного батча)                                                                                                                                                              | 1         |
| `Wallets`      | Create · Qr · Block · RefundBlockedDeposit                                                                                                                                                      | 4         |
| `Webhooks`     | Register · RotateSecret · Deliveries · Test (payment/payout/wallet) · TestLegacy                                                                                                                | 7         |
| `Documents`    | Statement · Ledger · BalanceCertificate · FeeSchedule · SplitReport · BatchReport · LinkReport · WalletStatement · ReferralsReport · CreateJob · JobInfo · JobFile · Download                    | 13        |
| `Splits`       | CreateRule · ListRules · DeleteRule · Get/SetConfig · Get/SetOptIn                                                                                                                               | 7         |
| `Settings`     | SetDiscount · ListDiscounts · Get/SetAccuracy · Get/SetAutoRefund · ListAccepted · SetAccepted · Get/SetPaymentFeeConfig · List/Set/DeleteAutoWithdraw · List/Add/Remove/EnableApiAllowlist      | 17        |
| `Account`      | Balance · Referral · Vrcs (без аргумента — чтение, с аргументом — запись)                                                                                                                       | 3         |
| `Catalog`      | Currencies · ExchangeRates                                                                                                                                                                      | 2         |
| `Sandbox`      | Faucet · Deposit · Webhooks · Replay · Reset                                                                                                                                                    | 5         |
| `Merchants`    | Create · CreateSandbox (заведение мерчантов; `AdminToken` на self-hosted шлюзе)                                                                                                                 | 2         |

Поиск принимает голый id, объект-ключ или сам объект, который у вас уже есть:
`Payments.InfoAsync("uuid")`, `Payments.InfoAsync(new PaymentLookup { OrderId = "order-1001" })`,
`Payouts.CancelAsync(payout)`, `PayoutLinks.CancelAsync(link)`. Синхронные массовые вызовы
(`Payouts.MassAsync` ≤ 100, `PayoutLinks.BatchAsync` ≤ 500) отвечают поэлементно —
`BatchElement<T> { Idx, Ok, Result, Message }`; асинхронные (`Payments.BatchAsync`,
`Payouts.BatchAsync`, `Refunds.BatchAsync`, `Transfers.BatchAsync`, ≤ 5000) опрашиваются через
`Batches.InfoAsync`. Документные маршруты отвечают вне JSON-конверта и возвращают
`FileResult { Bytes, ContentType, Filename }`. Отмена остаётся вашей: отмена `CancellationToken`
бросает `OperationCanceledException`, а не ошибку SDK, так что обрыв запроса в ASP.NET ведёт себя
так, как ожидает остальной ваш код.

### Списки

Списочный метод возвращает `PagePromise<T>`, который ещё ничего не запросил: `await` — одна
страница, `await foreach` — обход всех элементов по страницам, `AllAsync(max)` — собрать в список.

```csharp
using Oblodai.Contract;
using Oblodai.Models;

Page<Payment> page = await oblodai.Payments.HistoryAsync(new PaymentHistoryRequest { Limit = 50 });
Console.WriteLine($"{page.Items.Count} of {page.Paginate.Total}, more: {page.Paginate.HasPages}");

await foreach (var payout in oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Status = PayoutStatus.Confirmed }))
{
    Console.WriteLine(payout.Uuid);
}

List<Payout> refunds = await oblodai.Payouts.HistoryAsync(new PayoutHistoryRequest { Kind = "refund" }).AllAsync(1000);
```

### Статусы

- Платёж: `select → created → confirm_check → paid | paid_over | wrong_amount | expired | cancelled`.
  `Statuses.IsPaymentPaid(status)` истинно для `paid`/`paid_over`; `wrong_amount` (недоплата) ждёт
  `Refunds.ResolveAsync(new PaymentResolveRequest { Uuid = …, Action = "accept" })`;
  `Statuses.IsPaymentFinal` покрывает остальные.
- Выплата: `pending → approved → awaiting_cosign → broadcasting → sent → confirmed | failed | cancelled`.

Статусы, сети и остальные справочники — это `readonly record struct` поверх строки, а не C#-enum:
сравнивайте с константами (`PaymentStatus.Paid`), а значение новее этого снимка всё равно пройдёт
туда и обратно — `status.IsKnown` скажет, документирует ли его SDK. Для изменений состояния
предпочитайте вебхуки; `InfoAsync` опрашивайте только как запасной путь.

### Помощники для денег

`Money.Add`, `Money.Subtract`, `Money.Compare`, `Money.AreEqual`, `Money.IsZero` — точная десятичная
арифметика над строковыми суммами, которыми оперирует API. Никогда не разбирайте сумму с провода в
`double`: у USDT 6 знаков, у BTC 8, у ETH 18, и двоичная плавающая точка не хранит их точно. И
никогда не сравнивайте две суммы как строки — `"9" > "10"` истинно как текст и ложно как деньги. Всё,
что не `[-]digits[.digits]` длиной не больше 64 символов, отвергается через `ConfigException` /
`sdk.bad_amount` — собственной ошибкой SDK, а не `FormatException`.

## Вебхуки

`Webhooks.RegisterAsync(url)` задаёт (или заменяет) эндпоинт и возвращает секрет подписи — он
показывается один раз, так что сохраните его туда, откуда его прочитает приёмник. Проверке не нужны
ни клиент, ни API-ключ:

```csharp
using Oblodai;

var info = WebhookVerifier.VerifyDelivery(rawBody, header, new WebhookVerifyOptions
{
    Secret = Environment.GetEnvironmentVariable("OBLODAI_WEBHOOK_SECRET")!,
});

if (info.IsTest) // a rehearsal delivery: signed like a live one, but no money moved
{
    return;
}

switch (info.Event)
{
    case PaymentEvent { Status.Value: "paid" } paid: MarkOrderPaid(paid.OrderId, info.Id); break;
    case PayoutEvent payout: Track(payout.Uuid, payout.Status); break;
    case WalletEvent deposit: Credit(deposit.Address, deposit.PaymentAmount); break;
}
```

Проверяйте по **сырым** байтам — заново сериализованный разбор не совпадёт. `rawBody` — тело запроса
как оно пришло, `header` — регистронезависимый поиск заголовка (есть и перегрузка под
`IReadOnlyDictionary<string, string>`). MAC проверяется **раньше** таймстампа, поэтому окно
свежести нельзя прощупать неаутентифицированным отправителем. `WebhookVerifyOptions` отвергает
пустой `Secret`, пустой `PreviousSecret` и отрицательный `ToleranceSeconds` через `ConfigException`
ещё до любой криптографии; допуск по умолчанию — 300 секунд, а `ToleranceSeconds = 0` отключает
проверку свежести. Заголовок подписи принимается обрезанным и в любом регистре; префикс `0x` — не та
кодировка, которую шлёт шлюз, и он отвергается.

Правило кода ответа для приёмника: отвечайте 401 (или любым 4xx) **только** когда проверка не
прошла — то есть на подделку или на протухшую доставку, которая приходит как `SignatureException`.
Доставка, которая проверку прошла, но не читается, — это `WebhookPayloadException` /
`webhook.bad_payload`, ошибка *контракта*, а не подписи: отвечайте 5xx, потому что событие
настоящее и шлюз повторит его. Семейство событий, которого этот снимок не знает, приходит как
`UnknownWebhookEvent` с сырым `type` вместо исключения — различить помогает
`WebhookVerifier.IsKnownEvent`, а `IsTest`, `IsStale` и `IsKnownEvent` работают и на нём.

Репетиционные доставки (`Webhooks.TestAsync`, песочница) подписаны в точности как боевые и несут
`test: true` в теле (и `X-Webhook-Test: true`): проверяйте `info.IsTest` (или
`WebhookVerifier.IsTestEvent(info.Event)`) и никогда не считайте такую доставку движением денег.
`info.Id` (`X-Webhook-Id`) стабилен между ретраями — используйте его для дедупликации;
`WebhookVerifier.IsStale(info.Event, lastSequence)` отбрасывает пришедшую не по порядку доставку.
После `Webhooks.RotateSecretAsync` держите старый секрет в `PreviousSecret` не меньше 26 часов:
доставки, поставленные в очередь до ротации, остаются подписанными им всю свою жизнь ретраев.

## Ошибки

Любая неудача — это `OblodaiException` с конвертом ошибки API. Ветвитесь по `Code` — стабильной
строке вида `family.reason`, — а не по сообщению.

| Исключение                     | HTTP           | Когда                                                            |
| ------------------------------ | -------------- | ------------------------------------------------------------------ |
| `ValidationException`          | 400            | кривой запрос или бизнес-правило; `Field` называет виновника        |
| `AuthenticationException`      | 401            | плохая подпись, неизвестный ключ, расхождение часов, IP не в списке |
| `PermissionException`          | 403            | ключ валиден, но здесь нельзя (фича для вас выключена)              |
| `NotFoundException`            | 404            | у этого мерчанта такого объекта нет                                 |
| `ConflictException`            | 409            | конфликт состояния                                                  |
| `IdempotencyConflictException` | 409            | `idempotency.key_reused`: тот же ключ, другое тело                  |
| `RateLimitException`           | 429            | лимит запросов; `RetryAfter` заполнен                               |
| `UnavailableException`         | 503            | зависимость наверху лежит; безопасно повторить после паузы          |
| `InternalException`            | прочие 5xx     | шлюз упал                                                           |
| `ApiException`                 | всё остальное  | ошибочный статус с конвертом                                        |
| `TransportException`           | —              | ответа не было вовсе: DNS, TCP, TLS, таймаут, отмена                |
| `ConfigException`              | —              | отвергнуто до отправки: плохие опции, нет учётных данных            |
| `ContractException`            | —              | ответ не является документированным конвертом                       |
| `WebhookPayloadException`      | —              | доставка прошла проверку, но её не удалось прочитать                |
| `SignatureException`           | —              | проверка вебхука не прошла                                          |

Поля: `Code`, `Message`, `HttpStatus`, `Retryable` (авторитетно — SDK уже повторил то, что следовало),
`RetryAfter` (секунды), `RequestId` (называйте его поддержке), `Field` (на 400), `Synthetic` (ответ
пришёл от прокси, а не от API), `Family`.

```csharp
using Oblodai;
using Oblodai.Contract;

try
{
    var payout = await oblodai.Payouts.CreateAsync(request);
    Console.WriteLine($"{payout.Uuid} {payout.Status}");
}
catch (OblodaiException error)
    when (error.Code is ErrorCodes.PayoutInsufficientFunds or ErrorCodes.PayoutFundsMaturing)
{
    ScheduleRetry(error.RetryAfter ?? 60); // retryable — the balance may still arrive
}
catch (OblodaiException error)
{
    Log(error.Code, error.RequestId); // the SDK already retried whatever was safe to retry
}
```

Каталог — это `ErrorCodes`: все 469 кодов, которыми может ответить шлюз, поставляются со снимком
контракта и доступны константами (`ErrorCodes.PayoutInsufficientFunds`) плюс `ErrorCodes.All`. Коды,
которые стоит обработать в первую очередь: `payout.insufficient_funds` и `payout.funds_maturing`
(оба retryable), `idempotency.key_reused`, `invoice.not_payable`, `payment.not_found`,
`merchant.bad_signature`, `request.rate_limited`. Поверх них SDK поднимает
свои семейства: `sdk.missing_credentials`, `sdk.bad_config`, `sdk.bad_idempotency_key`,
`sdk.idempotency_unsupported`, `sdk.bad_envelope`, `sdk.bad_path_param`, `sdk.bad_amount`,
`sdk.bad_header`, `sdk.response_too_large`, `transport.timeout|network|deadline`,
`webhook.bad_signature|stale_timestamp|missing_header|bad_payload`.

Конверт ошибки читается по полю за раз: `retryable`, который не булев, откатывается к статусу;
`retry_after` в виде дроби, числовой строки или абсурдного числа зажимается в `[0, 86400]` секунд; а
тело без пригодного `code` становится синтетической ошибкой, несущей HTTP-статус. Кривой конверт
никогда не превращает повторяемый 503 в падение разбора. `error.ToJson()` сохраняет идентичность
(код, сообщение, статус, request id) и выбрасывает сырое тело, так что структурный лог не утечёт
того, что в теле было; `ToString()` сохраняет стек и внутреннее исключение.

## Ретраи, идемпотентность и таймауты

- **Безопасность повтора** не угадывается: можно ли переотправить маршрут — это собственный флаг
  `safe` шлюза, прочитанный из `contract/contract.json`, и кодоген падает на снимке, где его нет.
- Ошибка повторяется, только если API сказал `retryable: true`. Ответы без конверта API (502/503 от
  прокси) и транспортные сбои повторяются только на читающих маршрутах и на записи с ключом
  идемпотентности. `Retry-After` имеет приоритет над вычисленной паузой.
- **Ключи идемпотентности** проставляются автоматически на создающих маршрутах — один на логический
  вызов, переиспользуемый на каждом ретрае, — так что таймаут не может породить вторую выплату.
  Передайте свой (`new RequestOptions { IdempotencyKey = … }`), чтобы ретраи были безопасны и через
  перезапуск процесса; на маршрутах, которые шлюз не дедуплицирует (включая списочные методы), SDK
  отвергает ключ с `sdk.idempotency_unsupported`, а не позволяет вам поверить, что переотправка
  безопасна. Ключ, который нельзя отправить заголовком, — это `ConfigException` /
  `sdk.bad_idempotency_key`.
- **На вызов:** `RequestOptions { IdempotencyKey, TimeoutMs, DeadlineMs, Headers }` —
  `Headers` подмешиваются поверх клиентских только для этого вызова. **На клиент:** `TimeoutMs` (на
  попытку, 30 с), `DeadlineMs` (попытки вместе с паузами, 90 с), `Retry = new RetryOptions
  { MaxRetries = 2, BaseDelayMs = 250, MaxDelayMs = 4000, MaxRetryAfterMs = 30000 }`;
  `new RetryOptions { MaxRetries = 0 }` отключает ретраи. `Retry-After`, о котором сообщает шлюз,
  хранится вплоть до суток, но пауза, которую SDK реально берёт, никогда не превышает
  `MaxRetryAfterMs`.
- **Расхождение часов** корректируется по заголовку `Date` от API после 401, похожего на перекос, и
  коррекция откатывается, если не помогла; смещение больше 24 часов — неправдоподобный дрейф, оно
  игнорируется.
- **Редиректы никогда не выполняются**: подписанный запрос не должен переигрываться на другой origin,
  поэтому SDK замечает пройденный редирект (ответ вернулся с URL, на который он не отправлял) и
  роняет вызов, вместо того чтобы дать вашей подписи и телу уйти на чужой хост.
- **Ограничения на размер тела**: 8 MiB на JSON-маршрутах, 64 MiB на документных — ответ больше этого
  становится `sdk.response_too_large`, ошибкой контракта, а не исчерпанием памяти. Дедлайн покрывает
  всё чтение, а не только первый байт.
- **Зарезервированные заголовки** побеждают `Headers` и сравниваются без учёта регистра:
  `X-Public-Id`, `X-Signature`, `X-Timestamp`, `Idempotency-Key`, `X-Admin-Token`, `Accept`,
  `User-Agent`, `Content-Type`, `Content-Length`, `Host`. Заголовок с CR, LF или не-ASCII байтом
  отвергается через `sdk.bad_header` ещё до того, как что-то будет подписано.

## Конфигурация

| Опция                             | Что делает                                                                 |
| --------------------------------- | ---------------------------------------------------------------------------- |
| `PublicId` / `Secret`             | пара ключей API мерчанта; ею подписывается каждый подписанный маршрут       |
| `BaseUrl`                         | origin API; префикс пути сохраняется                                        |
| `AllowInsecureBaseUrl`            | разрешить обычный `http://` для не-loopback хоста                           |
| `AdminToken`                      | админ-токен онбординга self-hosted шлюза (только маршруты заведения)         |
| `TimeoutMs`                       | таймаут на попытку (по умолчанию 30000)                                     |
| `DeadlineMs`                      | бюджет одного вызова вместе с ретраями и паузами (по умолчанию 90000)       |
| `Retry`                           | политика ретраев; `new RetryOptions { MaxRetries = 0 }` их отключает        |
| `Logger`                          | структурный логгер для диагностики SDK                                      |
| `Headers`                         | заголовки на каждый запрос (зарезервированные имена игнорируются)           |
| `Clock`                           | часы подписи; подменяются в тестах                                          |

| Переменная окружения       | Значение                                                          |
| -------------------------- | ------------------------------------------------------------------- |
| `OBLODAI_PUBLIC_ID`        | public id ключа API                                                 |
| `OBLODAI_SECRET`           | секрет ключа API                                                    |
| `OBLODAI_ADMIN_TOKEN`      | админ-токен онбординга self-hosted шлюза                            |
| `OBLODAI_BASE_URL`         | origin API (по умолчанию `https://api.oblodai.com`)                 |
| `OBLODAI_LOG`              | `debug` \| `info` \| `warn` \| `error` — включает консольный логгер  |
| `OBLODAI_ALLOW_INSECURE`   | `1` разрешает обычный `http://` в base URL                          |

Явные опции побеждают окружение. Половина пары (id без секрета или наоборот) отвергается при
разрешении опций с кодом `sdk.bad_config`; отсутствие учётных данных всплывает позже — на первом
вызове, которому они нужны.

**Секреты не печатаются.** Секрет API-ключа, секрет вебхука, пасскод чека и клейм-токен или URL
показываются как `[redacted]` и в `ToString()`, и в стандартном пути `System.Text.Json` — оба пути по
умолчанию переопределены, так что ни строка лога, ни сериализованный дамп их не утекут. Чтение
свойства по-прежнему отдаёт значение, а `OblodaiJson.SerializeWithSecrets(model)` — единственный явный
способ сериализовать настоящее: для кода, который сохраняет секрет или отправляет клейм-URL письмом,
но никогда для лога.

**Внедрение зависимостей.** Клиент принимает `HttpClient`, которым управляете вы, поэтому он ложится
на `IHttpClientFactory`:

```csharp
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Oblodai;

services.AddHttpClient("oblodai").ConfigurePrimaryHttpMessageHandler(
    () => new SocketsHttpHandler { AllowAutoRedirect = false });

services.AddSingleton(sp => new OblodaiClient(
    new OblodaiOptions { PublicId = publicId, Secret = secret },
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("oblodai")));
```

SDK применяет собственный таймаут на попытку и дедлайн на вызов, поэтому ставьте
`HttpClient.Timeout` в `Timeout.InfiniteTimeSpan`. Конечный таймаут молча перебивает оба, и, увидев
такой, SDK пишет предупреждение с вашим значением и своим собственным.

**Self-hosted или локальный шлюз.** `BaseUrl = "http://127.0.0.1:8095"` работает из коробки; любому
другому http-хосту нужен `AllowInsecureBaseUrl = true` (или `OBLODAI_ALLOW_INSECURE=1`). Префикс
пути в base URL сохраняется, так что `https://gw.corp/oblodai` попадёт в
`https://gw.corp/oblodai/v1/payment` — и подпись покрывает путь вместе с префиксом.

## Снимок контракта

`contract/` выгружается собственным тестовым набором шлюза: реестр маршрутов (107 мерчантских
маршрутов, у каждого — вид аутентификации, обёртка идемпотентности и собственный флаг `safe` шлюза),
схемы DTO запросов с английскими описаниями полей, все справочники и все 469 кодов ошибок, векторы
подписи, эталонные тела ответов, записанные с живого шлюза, и настоящие подписанные доставки
вебхуков. `src/Oblodai/Contract/*.g.cs` генерируется из него и никогда не правится руками;
`ContractVersion.CoreCommit`, `ContractVersion.ExportedAt` и `ContractVersion.Hash` опознают
используемый снимок. Кодоген падает, а не гадает, если маршрут вдруг приедет без флага `safe`.

```bash
dotnet run --project tools/Codegen -- generate   # regenerate after refreshing contract/
dotnet run --project tools/Codegen -- check      # drift gate: fails when the generated files are stale
```

Контрактный ярус набора — это проверка полноты, а не выборка: у каждого из 107 маршрутов должен быть
метод, привязанный к правильному пути, шлюзу аутентификации и поведению идемпотентности, а каждое
записанное тело ответа должно разбираться в модель, поля которой сходятся с проводом ключ в ключ.

## Разработка

```bash
git clone https://github.com/oblodai/oblodai-dotnet && cd oblodai-dotnet
dotnet run --project tools/Codegen -- check          # the committed *.g.cs still match contract/
dotnet build -c Release -warnaserror                 # library, tests, tools and examples
dotnet test -c Release                               # unit + contract tiers (hermetic)
OBLODAI_LIVE_URL=http://127.0.0.1:8095 dotnet test   # adds the live tier against a running gateway
dotnet pack src/Oblodai/Oblodai.csproj -c Release
```

Файлы исходников держатся в пределах ~400 строк, а тесты живут рядом с тем, что проверяют. См.
[AGENTS.md](AGENTS.md) — та же поверхность на одной странице, написанная для кодовых агентов;
[CHANGELOG.md](CHANGELOG.md) — что менялось; [MIGRATION-1.3.md](MIGRATION-1.3.md) — переход с 1.2.

## License

MIT — см. [LICENSE](LICENSE).
