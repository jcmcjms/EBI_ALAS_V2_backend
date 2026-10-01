using System.Security.Cryptography;
namespace EBI.ALAS.Api.Features.Users;
public interface ITempPasswordGenerator
{
    string Generate(int length = 12);
}
public sealed class TempPasswordGenerator : ITempPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Special = "!?*.";
    private const string All = Upper + Lower + Digits + Special;
    public string Generate(int length = 12)
    {
        length = Math.Clamp(length, 8, 64);
        var chars = new char[length];
        var pools = new[] { Upper, Lower, Digits, Special };
        for (var i = 0; i < pools.Length; i++)
            chars[i] = pools[i][RandomNumberGenerator.GetInt32(pools[i].Length)];
        for (var i = pools.Length; i < length; i++)
            chars[i] = All[RandomNumberGenerator.GetInt32(All.Length)];
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
