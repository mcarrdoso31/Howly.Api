using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BZ.Howly.Api.DAO.Configurations;

/// <summary>Stores small value lists as JSON text, the same way on every database.</summary>
internal static class Json
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<T, string> Converter<T>() where T : class =>
        new(value => JsonSerializer.Serialize(value, Options), text => JsonSerializer.Deserialize<T>(text, Options)!);

    public static ValueComparer<IReadOnlyList<TItem>> Comparer<TItem>() =>
        new((a, b) => a!.SequenceEqual(b!), list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)), list => list.ToList());
}
