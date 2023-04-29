using System;
using System.Collections.Generic;
using System.Linq;

namespace AnIRC.Tests;

public static class Extensions {
	public static string JoinDictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary) where TKey : IComparable<TKey>
		=> JoinDictionary(dictionary, ',', ':');
	public static string JoinDictionary<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dictionary, char entryDelimiter, char keyValueDelimiter) where TKey : IComparable<TKey>
		=> string.Join(entryDelimiter, dictionary.OrderBy(e => e.Key).Select(e => $"{e.Key}{keyValueDelimiter}{e.Value}"));
}
