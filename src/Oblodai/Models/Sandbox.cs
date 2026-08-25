using System.Text.Json.Serialization;

namespace Oblodai.Models;

/// <summary><c>/v1/sandbox/faucet</c> — test funds credited to the dev store's balance.</summary>
public sealed record FaucetResult
{
    /// <summary>Asset that was credited.</summary>
    [JsonPropertyName("asset")]
    public string Asset { get; init; } = string.Empty;

    /// <summary>How much was credited. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>The ledger entry the credit was written as.</summary>
    [JsonPropertyName("journal_id")]
    public string JournalId { get; init; } = string.Empty;
}

/// <summary><c>/v1/sandbox/deposit</c> — a synthetic deposit against a sandbox invoice.</summary>
public sealed record SandboxDeposit
{
    /// <summary>The invoice that was paid.</summary>
    [JsonPropertyName("invoice_id")]
    public string InvoiceId { get; init; } = string.Empty;

    /// <summary>How much was deposited. Decimal string.</summary>
    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;

    /// <summary>Confirmations the synthetic transfer was given.</summary>
    [JsonPropertyName("confirmations")]
    public int Confirmations { get; init; }

    /// <summary>The synthetic transaction hash.</summary>
    [JsonPropertyName("txid")]
    public string Txid { get; init; } = string.Empty;
}

/// <summary><c>/v1/sandbox/reset</c> — what the wipe touched.</summary>
public sealed record SandboxReset
{
    /// <summary>How many open invoices were cancelled.</summary>
    [JsonPropertyName("invoices_cancelled")]
    public int InvoicesCancelled { get; init; }

    /// <summary>How many balance rows were zeroed.</summary>
    [JsonPropertyName("balances_zeroed")]
    public int BalancesZeroed { get; init; }
}

/// <summary><c>/v1/sandbox/webhooks/replay</c> — a recorded delivery was queued again.</summary>
public sealed record SandboxReplay
{
    /// <summary>True — the replay was accepted.</summary>
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    /// <summary>The delivery that was replayed.</summary>
    [JsonPropertyName("delivery_id")]
    public string DeliveryId { get; init; } = string.Empty;
}
