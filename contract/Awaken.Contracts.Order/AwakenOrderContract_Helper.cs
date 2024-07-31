using System.Linq;
using AElf.Contracts.MultiToken;
using AElf.Types;

namespace Awaken.Contracts.Order;

public partial class AwakenOrderContract
{
    private string GetTokenPair(string tokenA, string tokenB)
    {
        var symbols = SortSymbols(tokenA, tokenB);
        return $"{symbols[0]}-{symbols[1]}";
    }
    
    private string[] SortSymbols(params string[] symbols)
    {
        Assert(
            symbols.Length == 2 && !symbols.First().All(IsValidItemIdChar) &&
            !symbols.Last().All(IsValidItemIdChar), "Invalid symbols for sorting.");
        return symbols.OrderBy(s => s).ToArray();
    }
    
    private bool IsValidItemIdChar(char character)
    {
        return character >= '0' && character <= '9';
    }
    
    public static long IntPow(int x, int y)
    {
        long result = 1;
        for (int i = 0; i < y; i++)
        {
            result *= x;
        }
        return result;
    }
    
    private void AssertAllowanceAndBalance(string symbol, long amount)
    {
        var allowance = State.TokenContract.GetAllowance.Call(new GetAllowanceInput
        {
            Spender = Context.Self,
            Owner = Context.Sender,
            Symbol = symbol
        });
        Assert(allowance.Allowance >= amount, "Allowance not enough");
        var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput()
        {
            Owner = Context.Sender,
            Symbol = symbol
        });
        Assert(balance.Balance >= amount, "Balance not enough");
    }
    
    private bool CheckAllowanceAndBalance(Address owner, string symbol, long amount)
    {
        var allowance = State.TokenContract.GetAllowance.Call(new GetAllowanceInput
        {
            Spender = Context.Self,
            Owner = owner,
            Symbol = symbol
        });
        if (allowance.Allowance < amount)
        {
            return false;
        }
        var balance = State.TokenContract.GetBalance.Call(new GetBalanceInput()
        {
            Owner = owner,
            Symbol = symbol
        });
        return balance.Balance > amount;
    }
}