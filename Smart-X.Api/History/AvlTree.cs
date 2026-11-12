namespace Smart_X.Api.History
{
    /// <summary>
    /// A self-balancing binary search tree (AVL). Every node keeps its subtrees' heights within 1 of each other,
    /// so the tree never leans into a list: its height stays under about 1.44 * log2(n), and lookups, inserts and
    /// removals cost O(log n) however the keys arrive. Timestamps arrive in increasing order, which turns a plain
    /// binary search tree into a linked list; the rotations below are what prevent that.
    ///
    /// Each node also stores how many nodes are in its subtree. That makes "how many keys fall in this range" an
    /// O(log n) walk, and lets a page of results start at any position without counting past the earlier ones.
    ///
    /// Keys are unique. Not thread-safe: callers lock around it.
    /// </summary>
    public class AvlTree<TKey, TValue> where TKey : IComparable<TKey>
    {
        private sealed class Node
        {
            public Node(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }

            public TKey Key;
            public TValue Value;
            public Node? Left;
            public Node? Right;
            public int Height = 1;
            public int Size = 1;
        }

        private Node? _root;

        public int Count => SizeOf(_root);

        /// <summary>Levels from the root down to the deepest node. 0 for an empty tree.</summary>
        public int Height => HeightOf(_root);

        /// <summary>False, and nothing changes, when the key is already there.</summary>
        public bool Add(TKey key, TValue value)
        {
            var added = false;
            _root = Insert(_root, key, value, ref added);
            return added;
        }

        /// <summary>False when there was no such key.</summary>
        public bool Remove(TKey key)
        {
            var removed = false;
            _root = Delete(_root, key, ref removed);
            return removed;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            for (var node = _root; node is not null;)
            {
                var order = key.CompareTo(node.Key);
                if (order == 0)
                {
                    value = node.Value;
                    return true;
                }
                node = order < 0 ? node.Left : node.Right;
            }

            value = default!;
            return false;
        }

        public bool ContainsKey(TKey key) => TryGetValue(key, out _);

        public bool TryGetMin(out TKey key, out TValue value)
        {
            if (_root is null)
            {
                key = default!;
                value = default!;
                return false;
            }

            var node = _root;
            while (node.Left is not null) node = node.Left;
            key = node.Key;
            value = node.Value;
            return true;
        }

        /// <summary>How many keys are strictly less than this one.</summary>
        public int CountLess(TKey key)
        {
            var count = 0;
            for (var node = _root; node is not null;)
            {
                if (key.CompareTo(node.Key) <= 0)
                {
                    node = node.Left;
                }
                else
                {
                    // This node and everything in its left subtree are smaller: count them without visiting them.
                    count += 1 + SizeOf(node.Left);
                    node = node.Right;
                }
            }
            return count;
        }

        /// <summary>How many keys are less than or equal to this one.</summary>
        public int CountLessOrEqual(TKey key)
        {
            var count = 0;
            for (var node = _root; node is not null;)
            {
                if (key.CompareTo(node.Key) < 0)
                {
                    node = node.Left;
                }
                else
                {
                    count += 1 + SizeOf(node.Left);
                    node = node.Right;
                }
            }
            return count;
        }

        /// <summary>How many keys lie between from and to, both included. 0 when from is after to.</summary>
        public int CountBetween(TKey from, TKey to) => from.CompareTo(to) > 0 ? 0 : CountLessOrEqual(to) - CountLess(from);

        /// <summary>
        /// Up to count entries in key order, starting at the entry in position rank (0 is the smallest key). Finding
        /// the start is O(log n); every entry after that is O(1). Lazy: nothing is walked until you enumerate it.
        /// </summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> Slice(int rank, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rank);
            if (count <= 0 || rank >= Count) yield break;

            // Walk down to the entry at "rank", remembering the nodes we turned left at: they are the ones still to come.
            var pending = new Stack<Node>();
            var remaining = rank;
            for (var node = _root; node is not null;)
            {
                var smaller = SizeOf(node.Left);
                if (remaining < smaller)
                {
                    pending.Push(node);
                    node = node.Left;
                }
                else if (remaining == smaller)
                {
                    pending.Push(node);
                    break;
                }
                else
                {
                    remaining -= smaller + 1;
                    node = node.Right;
                }
            }

            while (count > 0 && pending.Count > 0)
            {
                var current = pending.Pop();
                yield return new KeyValuePair<TKey, TValue>(current.Key, current.Value);
                count--;

                // The next entries are the right subtree, smallest first: its left edge.
                for (var next = current.Right; next is not null; next = next.Left) pending.Push(next);
            }
        }

        /// <summary>Every entry with from <= key <= to, in key order. O(log n) to find, then O(1) per result.</summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> Between(TKey from, TKey to) =>
            Slice(CountLess(from), CountBetween(from, to));

        /// <summary>One page of the entries between from and to: skip the first few, take at most take.</summary>
        public IEnumerable<KeyValuePair<TKey, TValue>> Between(TKey from, TKey to, int skip, int take)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(skip);
            var inRange = CountBetween(from, to);
            return Slice(CountLess(from) + skip, Math.Min(take, inRange - skip));
        }

        public KeyValuePair<TKey, TValue> ElementAt(int rank)
        {
            if (rank < 0 || rank >= Count) throw new ArgumentOutOfRangeException(nameof(rank));
            return Slice(rank, 1).First();
        }

        /// <summary>
        /// Throws if any AVL rule is broken: keys in order, stored heights and sizes correct, and no node leaning
        /// by more than one level. Walks the whole tree, so it is for tests.
        /// </summary>
        public void AssertInvariants() => Check(_root, default, false, default, false);

        private (int Height, int Size) Check(Node? node, TKey? low, bool hasLow, TKey? high, bool hasHigh)
        {
            if (node is null) return (0, 0);

            if (hasLow && node.Key.CompareTo(low!) <= 0) throw new InvalidOperationException($"{node.Key} should be greater than {low}");
            if (hasHigh && node.Key.CompareTo(high!) >= 0) throw new InvalidOperationException($"{node.Key} should be less than {high}");

            var left = Check(node.Left, low, hasLow, node.Key, true);
            var right = Check(node.Right, node.Key, true, high, hasHigh);

            var height = 1 + Math.Max(left.Height, right.Height);
            var size = 1 + left.Size + right.Size;
            if (node.Height != height) throw new InvalidOperationException($"{node.Key}: stored height {node.Height}, real height {height}");
            if (node.Size != size) throw new InvalidOperationException($"{node.Key}: stored size {node.Size}, real size {size}");
            if (Math.Abs(left.Height - right.Height) > 1) throw new InvalidOperationException($"{node.Key}: subtree heights {left.Height} and {right.Height} differ by more than 1");

            return (height, size);
        }

        private static int HeightOf(Node? node) => node?.Height ?? 0;

        private static int SizeOf(Node? node) => node?.Size ?? 0;

        private static void Refresh(Node node)
        {
            node.Height = 1 + Math.Max(HeightOf(node.Left), HeightOf(node.Right));
            node.Size = 1 + SizeOf(node.Left) + SizeOf(node.Right);
        }

        // Called on the way back up after an insert or removal: fix this node's numbers, and rotate if it now leans by 2.
        private static Node Rebalance(Node node)
        {
            Refresh(node);
            var lean = HeightOf(node.Left) - HeightOf(node.Right);

            if (lean > 1)
            {
                // Left side too tall. If its taller part is on the inside (the left child leans right), straighten that first.
                if (HeightOf(node.Left!.Left) < HeightOf(node.Left.Right)) node.Left = RotateLeft(node.Left);
                return RotateRight(node);
            }

            if (lean < -1)
            {
                if (HeightOf(node.Right!.Right) < HeightOf(node.Right.Left)) node.Right = RotateRight(node.Right);
                return RotateLeft(node);
            }

            return node;
        }

        //      node            pivot
        //     /    \          /     \
        //  pivot    C   =>   A      node
        //  /   \                    /   \
        // A     B                  B     C
        private static Node RotateRight(Node node)
        {
            var pivot = node.Left!;
            node.Left = pivot.Right;
            pivot.Right = node;
            Refresh(node);
            Refresh(pivot);
            return pivot;
        }

        private static Node RotateLeft(Node node)
        {
            var pivot = node.Right!;
            node.Right = pivot.Left;
            pivot.Left = node;
            Refresh(node);
            Refresh(pivot);
            return pivot;
        }

        // Recursion depth is the tree height, which is at most about 30 even for a million entries.
        private static Node Insert(Node? node, TKey key, TValue value, ref bool added)
        {
            if (node is null)
            {
                added = true;
                return new Node(key, value);
            }

            var order = key.CompareTo(node.Key);
            if (order < 0) node.Left = Insert(node.Left, key, value, ref added);
            else if (order > 0) node.Right = Insert(node.Right, key, value, ref added);
            else return node;

            return added ? Rebalance(node) : node;
        }

        private static Node? Delete(Node? node, TKey key, ref bool removed)
        {
            if (node is null) return null;

            var order = key.CompareTo(node.Key);
            if (order < 0)
            {
                node.Left = Delete(node.Left, key, ref removed);
            }
            else if (order > 0)
            {
                node.Right = Delete(node.Right, key, ref removed);
            }
            else
            {
                removed = true;
                if (node.Left is null) return node.Right;
                if (node.Right is null) return node.Left;

                // Two children: take the next key up (the smallest in the right subtree) and remove it from there.
                var next = node.Right;
                while (next.Left is not null) next = next.Left;
                node.Key = next.Key;
                node.Value = next.Value;
                node.Right = DeleteMin(node.Right);
            }

            return removed ? Rebalance(node) : node;
        }

        private static Node? DeleteMin(Node node)
        {
            if (node.Left is null) return node.Right;

            node.Left = DeleteMin(node.Left);
            return Rebalance(node);
        }
    }
}
