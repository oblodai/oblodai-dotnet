using Oblodai.Models;

namespace Oblodai;

/// <summary>A binary response (PDF/CSV documents).</summary>
/// <param name="Bytes">The document.</param>
/// <param name="ContentType">MIME type the gateway sent.</param>
/// <param name="Filename">Name from <c>Content-Disposition</c>, when the gateway supplied one.</param>
public sealed record FileResult(byte[] Bytes, string ContentType, string? Filename);

/// <summary>Page window for list routes whose filter carries no <c>limit</c>/<c>offset</c> of its own.</summary>
public sealed record PageParams
{
    /// <summary>Rows per page.</summary>
    public int? Limit { get; init; }

    /// <summary>Row offset to start at.</summary>
    public int? Offset { get; init; }
}

/// <summary>
/// Identify an invoice by its <c>uuid</c> or by your <c>order_id</c> (one of them is required; the
/// <c>uuid</c> wins when both are set). A bare string converts to a lookup by <c>uuid</c>, so
/// <c>InfoAsync("9f4c…")</c> and <c>InfoAsync(new PaymentLookup { OrderId = "order-1" })</c> both work.
/// </summary>
public sealed record PaymentLookup
{
    /// <summary>Invoice id in Oblodai.</summary>
    public string? Uuid { get; init; }

    /// <summary>Your own order reference.</summary>
    public string? OrderId { get; init; }

    /// <summary>A bare string is taken as the <c>uuid</c>.</summary>
    /// <param name="uuid">Invoice id in Oblodai.</param>
    public static implicit operator PaymentLookup(string uuid) => new() { Uuid = uuid };

    /// <summary>An invoice you already have.</summary>
    /// <param name="payment">The invoice.</param>
    public static implicit operator PaymentLookup(Payment payment) => new() { Uuid = payment.Uuid };

    /// <summary>Named form of the string conversion.</summary>
    /// <param name="uuid">Invoice id in Oblodai.</param>
    public static PaymentLookup FromUuid(string uuid) => new() { Uuid = uuid };
}

/// <summary>
/// Identify a payout by its <c>uuid</c> or by your <c>order_id</c> (one of them is required; the
/// <c>uuid</c> wins when both are set). A bare string converts to a lookup by <c>uuid</c>.
/// </summary>
public sealed record PayoutLookup
{
    /// <summary>Payout id in Oblodai.</summary>
    public string? Uuid { get; init; }

    /// <summary>Your own order reference.</summary>
    public string? OrderId { get; init; }

    /// <summary>A bare string is taken as the <c>uuid</c>.</summary>
    /// <param name="uuid">Payout id in Oblodai.</param>
    public static implicit operator PayoutLookup(string uuid) => new() { Uuid = uuid };

    /// <summary>A payout you already have.</summary>
    /// <param name="payout">The payout.</param>
    public static implicit operator PayoutLookup(Payout payout) => new() { Uuid = payout.Uuid };

    /// <summary>Named form of the string conversion.</summary>
    /// <param name="uuid">Payout id in Oblodai.</param>
    public static PayoutLookup FromUuid(string uuid) => new() { Uuid = uuid };
}

/// <summary>Language of a generated document: a 2-letter code, one of the 41 supported (<c>en</c> by default).</summary>
public record DocumentQuery
{
    /// <summary>Document language, e.g. <c>"ru"</c>; the full list is in the <c>document.unknown_lang</c> error.</summary>
    public string? Lang { get; init; }
}

/// <summary>Adds the file format, where the document offers CSV as well as PDF.</summary>
public record FormatQuery : DocumentQuery
{
    /// <summary><c>"pdf"</c> (default) or <c>"csv"</c>.</summary>
    public string? Format { get; init; }
}

/// <summary>Adds the reporting period.</summary>
public record PeriodQuery : FormatQuery
{
    /// <summary>Start of the period, <c>YYYY-MM-DD</c>.</summary>
    public string? From { get; init; }

    /// <summary>End of the period, inclusive, <c>YYYY-MM-DD</c>.</summary>
    public string? To { get; init; }
}

/// <summary>The signature carried by a <c>document_url</c>: it authorises one public download.</summary>
public sealed record SignedDocumentQuery : DocumentQuery
{
    /// <summary>Expiry stamp from the <c>document_url</c>.</summary>
    public required long Exp { get; init; }

    /// <summary>Signature from the <c>document_url</c>.</summary>
    public required string Sig { get; init; }
}

/// <summary>
/// A payout, named either by its id or by the object you already hold. The reference implementation
/// takes <c>string | { uuid }</c> everywhere; these implicit conversions are the same affordance in C#,
/// so <c>CancelAsync(payout)</c> and <c>CancelAsync(payout.Uuid)</c> are both spelled the obvious way
/// and neither can be passed the wrong id by hand.
/// </summary>
public sealed record PayoutRef
{
    /// <summary>Payout id in Oblodai.</summary>
    public required string Uuid { get; init; }

    /// <summary>A bare id.</summary>
    /// <param name="uuid">Payout id.</param>
    public static implicit operator PayoutRef(string uuid) => new() { Uuid = uuid };

    /// <summary>A payout you already have.</summary>
    /// <param name="payout">The payout.</param>
    public static implicit operator PayoutRef(Payout payout) => new() { Uuid = payout.Uuid };

    /// <summary>Named form of the string conversion.</summary>
    /// <param name="uuid">Payout id.</param>
    public static PayoutRef FromUuid(string uuid) => new() { Uuid = uuid };
}

/// <summary>
/// A payout or payment link, named either by its id or by the object you already hold. Every model the
/// gateway hands back with a <c>link_id</c> converts.
/// </summary>
public sealed record LinkRef
{
    /// <summary>The link's id.</summary>
    public required string LinkId { get; init; }

    /// <summary>A bare id.</summary>
    /// <param name="linkId">Link id.</param>
    public static implicit operator LinkRef(string linkId) => new() { LinkId = linkId };

    /// <summary>A payout link (cheque) you already have.</summary>
    /// <param name="link">The link.</param>
    public static implicit operator LinkRef(PayoutLink link) => new() { LinkId = link.LinkId };

    /// <summary>A payment link you already have.</summary>
    /// <param name="link">The link.</param>
    public static implicit operator LinkRef(PaymentLink link) => new() { LinkId = link.LinkId };

    /// <summary>The acknowledgement <c>PaymentLinks.CreateAsync</c> returns.</summary>
    /// <param name="link">The created link.</param>
    public static implicit operator LinkRef(PaymentLinkCreated link) => new() { LinkId = link.LinkId };

    /// <summary>The acknowledgement <c>PaymentLinks.ToggleAsync</c> returns.</summary>
    /// <param name="link">The toggled link.</param>
    public static implicit operator LinkRef(PaymentLinkToggled link) => new() { LinkId = link.LinkId };

    /// <summary>The payer-facing view of a payment link.</summary>
    /// <param name="link">The public view.</param>
    public static implicit operator LinkRef(PublicPaymentLink link) => new() { LinkId = link.LinkId };

    /// <summary>Named form of the string conversion.</summary>
    /// <param name="linkId">Link id.</param>
    public static LinkRef FromId(string linkId) => new() { LinkId = linkId };
}
