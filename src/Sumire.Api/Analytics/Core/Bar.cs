namespace Sumire.Api.Analytics.Core;

/// <summary>One daily bar, oldest first in every list. AdjClose includes splits and dividends.</summary>
public record Bar(DateOnly Date, decimal Open, decimal High, decimal Low, decimal Close, decimal AdjClose, long Volume);
