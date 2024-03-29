﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace AnIRC;

/// <summary>Represents a read-only unordered collection of entities identified by a name.</summary>
[DebuggerDisplay("Count = {Count}")]
public class IrcNamedEntityCollection<T> : ICollection<T>, IReadOnlyCollection<T> where T : INamedEntity {
	/// <summary>Returns the <see cref="IrcClient"/> that this collection belongs to.</summary>
	protected IrcClient? Client { get; }
	/// <summary>Returns the underlying dictionary of this <see cref="IrcNamedEntityCollection{T}"/>.</summary>
	protected Dictionary<string, T> Dictionary { get; private set; }

	/// <summary>Initializes a new <see cref="IrcNamedEntityCollection{T}"/> belonging to the specified <see cref="IrcClient"/>.</summary>
	protected internal IrcNamedEntityCollection(IrcClient? client) {
		this.Client = client;
		this.Dictionary = new(client?.CaseMappingComparer ?? IrcStringComparer.RFC1459);
	}
	/// <summary>Initializes a new <see cref="IrcNamedEntityCollection{T}"/> using the specified <see cref="IEqualityComparer{T}"/>.</summary>
	protected internal IrcNamedEntityCollection(IEqualityComparer<string> comparer) => this.Dictionary = new(comparer);

	/// <summary>Returns the number of items in this collection.</summary>
	public int Count => this.Dictionary.Count;

	/// <summary>Returns the item with the specified name.</summary>
	/// <exception cref="KeyNotFoundException">There is no item with the specified name.</exception>
	/// <seealso cref="TryGetValue(string, out T)"/>
	public T this[string name] => this.Dictionary[name];

	/// <summary>Returns a collection containing the names in this collection.</summary>
	public Dictionary<string, T>.KeyCollection Keys => this.Dictionary.Keys;

	protected internal void Add(T entity) => this.Dictionary.Add(entity.Name, entity);
	protected internal void AddOrUpdate(T entity) => this.Dictionary[entity.Name] = entity;

	protected internal bool Remove(string name) => this.Dictionary.Remove(name);
	protected internal bool Remove(T entity) => this.Dictionary.Remove(entity.Name);
	protected internal void Clear() => this.Dictionary.Clear();

	/// <summary>Recomputes the hash codes in the collection based on the new <see cref="IrcStringComparer"/> used by the <see cref="IrcClient"/> associated with the collection.</summary>
	/// <exception cref="InvalidOperationException"><see cref="Client"/> is null or the new comparer causes multiple existing names to compare as equal.</exception>
	protected internal void UpdateCaseMapping() {
		if (this.Client is null) throw new InvalidOperationException($"{nameof(this.Client)} is null");
		if (this.Dictionary.Comparer == this.Client.CaseMappingComparer) return;
		try {
			this.Dictionary = new(this.Dictionary, this.Client.CaseMappingComparer);
		} catch (ArgumentException ex) {
			throw new InvalidOperationException(ex.Message, ex);
		}
	}

	/// <summary>Determines whether a <see cref="T"/> with the specified name is in this list.</summary>
	public bool Contains(string name) => this.Dictionary.ContainsKey(name);
	/// <summary>Determines whether the specified <see cref="T"/> is in this list.</summary>
	public bool Contains(T entity) => this.TryGetValue(entity.Name, out var existingEntity) && ReferenceEquals(entity, existingEntity);

	/// <summary>Attempts to get the <see cref="T"/> with the specified name and returns a value indicating whether it was found.</summary>
	/// <param name="name">The name to search for.</param>
	/// <param name="value">When this method returns, contains the <see cref="T"/> searched for, or null if no such <see cref="T"/> is in the list.</param>
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
	public bool TryGetValue(string name, [MaybeNullWhen(false)] out T value) => this.Dictionary.TryGetValue(name, out value);
#else
	public bool TryGetValue(string name, out T value) => this.Dictionary.TryGetValue(name, out value);
#endif

	/// <summary>Returns an enumerator that enumerates the <see cref="T"/>s in this list in an undefined order.</summary>
	public IEnumerator<T> GetEnumerator() => this.Dictionary.Values.GetEnumerator();

	/// <summary>Copies all of the <see cref="T"/>s in this list to the specified array in an undefined order, starting at the specified index in the target array.</summary>
	public void CopyTo(T[] array, int startIndex) => this.Dictionary.Values.CopyTo(array, startIndex);

	bool ICollection<T>.IsReadOnly => true;
	void ICollection<T>.Add(T entity) => throw new NotSupportedException("The collection is read-only.");
	void ICollection<T>.Clear() => throw new NotSupportedException("The collection is read-only.");
	bool ICollection<T>.Remove(T entity) => throw new NotSupportedException("The collection is read-only.");
	IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
