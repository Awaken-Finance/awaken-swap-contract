using AElf;
using AElf.Types;

namespace Awaken.Contracts.Points;

public partial class AwakenPointsContract
{
    private const long FeeRateMax = 10000;
    private const int PriceDataFeedsRequestTypeIndex = 1;

    private bool IsAddressValid(Address input)
    {
        return input != null && !input.Value.IsNullOrEmpty();
    }

    private bool IsHashValid(Hash input)
    {
        return input != null && !input.Value.IsNullOrEmpty();
    }

    private bool IsStringValid(string input)
    {
        return !string.IsNullOrWhiteSpace(input);
    }
    
    private long IntPow(int x, int y)
    {
        long result = 1;
        for (var i = 0; i < y; i++)
        {
            result *= x;
        }
        return result;
    }
}