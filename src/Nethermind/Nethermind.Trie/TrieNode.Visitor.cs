// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Serialization.Rlp;
using Nethermind.Trie.Pruning;

[assembly: InternalsVisibleTo("Ethereum.Trie.Test")]
[assembly: InternalsVisibleTo("Nethermind.Blockchain.Test")]
[assembly: InternalsVisibleTo("Nethermind.Trie.Test")]

namespace Nethermind.Trie
{
    public partial class TrieNode
    {
        private const int BranchesCount = 16;

        /// <summary>
        /// Like `Accept`, but does not execute its children. Instead it return the next trie to visit in the list
        /// `nextToVisit`. Also, it assume the node is already resolved.
        /// </summary>
        /// <param name="visitor"></param>
        /// <param name="nodeResolver"></param>
        /// <param name="trieVisitContext"></param>
        /// <param name="nextToVisit"></param>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="TrieException"></exception>
        internal void AcceptResolvedNode(
            ITreeVisitor visitor,
            ITrieNodeResolver nodeResolver,
            Hash256? address,
            in TreePath path,
            SmallTrieVisitContext trieVisitContext,
            IList<(Hash256?, TreePath, TrieNode, SmallTrieVisitContext)> nextToVisit
        )
        {
            switch (NodeType)
            {
                case NodeType.Branch:
                    {
                        visitor.VisitBranch(this, trieVisitContext.ToVisitContext());
                        trieVisitContext.Level++;

                        for (int i = 0; i < BranchesCount; i++)
                        {
                            TrieNode child = GetChild(nodeResolver, path, i);
                            if (child is not null)
                            {
                                child.ResolveKey(nodeResolver, GetChildPath(path, i), false);
                                if (visitor.ShouldVisit(child.Keccak!))
                                {
                                    SmallTrieVisitContext childCtx = trieVisitContext; // Copy
                                    childCtx.BranchChildIndex = (byte?)i;

                                    nextToVisit.Add((address, GetChildPath(path, i), child, childCtx));
                                }

                                if (child.IsPersisted)
                                {
                                    UnresolveChild(i);
                                }
                            }
                        }

                        break;
                    }
                case NodeType.Extension:
                    {
                        visitor.VisitExtension(this, trieVisitContext.ToVisitContext());
                        TrieNode child = GetChild(nodeResolver, path, 0);
                        if (child is null)
                        {
                            throw new InvalidDataException($"Child of an extension {Key} should not be null.");
                        }

                        child.ResolveKey(nodeResolver, GetChildPath(path, 0), false);
                        if (visitor.ShouldVisit(child.Keccak!))
                        {
                            trieVisitContext.Level++;
                            trieVisitContext.BranchChildIndex = null;

                            nextToVisit.Add((address, GetChildPath(path, 0), child, trieVisitContext));
                        }

                        break;
                    }

                case NodeType.Leaf:
                    {
                        visitor.VisitLeaf(this, trieVisitContext.ToVisitContext(), Value.ToArray());

                        if (!trieVisitContext.IsStorage && trieVisitContext.ExpectAccounts) // can combine these conditions
                        {
                            Account account = _accountDecoder.Decode(Value.AsRlpStream());
                            if (account.HasCode && visitor.ShouldVisit(account.CodeHash))
                            {
                                trieVisitContext.Level++;
                                trieVisitContext.BranchChildIndex = null;
                                visitor.VisitCode(account.CodeHash, trieVisitContext.ToVisitContext());
                                trieVisitContext.Level--;
                            }

                            if (account.HasStorage && visitor.ShouldVisit(account.StorageRoot))
                            {
                                trieVisitContext.IsStorage = true;
                                trieVisitContext.Level++;
                                trieVisitContext.BranchChildIndex = null;

                                if (TryResolveStorageRoot(nodeResolver, path, out TrieNode? chStorageRoot))
                                {
                                    TreePath storage = path.Append(Key);
                                    nextToVisit.Add((storage.Path.ToCommitment(), TreePath.Empty, chStorageRoot!, trieVisitContext));
                                }
                                else
                                {
                                    visitor.VisitMissingNode(account.StorageRoot, trieVisitContext.ToVisitContext());
                                }
                            }
                        }

                        break;
                    }

                default:
                    throw new TrieException($"An attempt was made to visit a node {Keccak} of type {NodeType}");
            }
        }

        internal void Accept(ITreeVisitor visitor, ITrieNodeResolver nodeResolver, in TreePath path, TrieVisitContext trieVisitContext)
        {
            try
            {
                ResolveNode(nodeResolver, path);
            }
            catch (TrieException)
            {
                visitor.VisitMissingNode(Keccak, trieVisitContext);
                return;
            }

            ResolveKey(nodeResolver, path, trieVisitContext.Level == 0);

            switch (NodeType)
            {
                case NodeType.Branch:
                    {
                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        void VisitChild(in TreePath parentPath, int i, TrieNode? child, ITrieNodeResolver resolver, ITreeVisitor v, TrieVisitContext context)
                        {
                            if (child is not null)
                            {
                                TreePath childPath = GetChildPath(parentPath, i);
                                child.ResolveKey(resolver, childPath, false);
                                if (v.ShouldVisit(child.Keccak!))
                                {
                                    context.BranchChildIndex = i;
                                    child.Accept(v, resolver, childPath, context);
                                }

                                if (child.IsPersisted)
                                {
                                    UnresolveChild(i);
                                }
                            }
                        }

                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        void VisitSingleThread(in TreePath parentPath, ITreeVisitor treeVisitor, ITrieNodeResolver trieNodeResolver, TrieVisitContext visitContext)
                        {
                            // single threaded route
                            for (int i = 0; i < BranchesCount; i++)
                            {
                                VisitChild(parentPath, i, GetChild(trieNodeResolver, parentPath, i), trieNodeResolver, treeVisitor, visitContext);
                            }
                        }

                        [MethodImpl(MethodImplOptions.AggressiveInlining)]
                        void VisitMultiThread(in TreePath parentPath, ITreeVisitor treeVisitor, ITrieNodeResolver trieNodeResolver, TrieVisitContext visitContext, TrieNode?[] children)
                        {
                            TreePath closureParentPath = parentPath;
                            // multithreaded route
                            Parallel.For(0, BranchesCount, i =>
                            {
                                visitContext.Semaphore.Wait();
                                try
                                {
                                    // we need to have separate context for each thread as context tracks level and branch child index
                                    TrieVisitContext childContext = visitContext.Clone();
                                    VisitChild(closureParentPath, i, children[i], trieNodeResolver, treeVisitor, childContext);
                                }
                                finally
                                {
                                    visitContext.Semaphore.Release();
                                }
                            });
                        }

                        visitor.VisitBranch(this, trieVisitContext);
                        trieVisitContext.AddVisited();
                        trieVisitContext.Level++;

                        if (trieVisitContext.MaxDegreeOfParallelism != 1 && trieVisitContext.Semaphore.CurrentCount > 1)
                        {
                            // we need to preallocate children
                            TrieNode?[] children = new TrieNode?[BranchesCount];
                            for (int i = 0; i < BranchesCount; i++)
                            {
                                children[i] = GetChild(nodeResolver, path, i);
                            }

                            if (trieVisitContext.Semaphore.CurrentCount > 1)
                            {
                                VisitMultiThread(path, visitor, nodeResolver, trieVisitContext, children);
                            }
                            else
                            {
                                VisitSingleThread(path, visitor, nodeResolver, trieVisitContext);
                            }
                        }
                        else
                        {
                            VisitSingleThread(path, visitor, nodeResolver, trieVisitContext);
                        }

                        trieVisitContext.Level--;
                        trieVisitContext.BranchChildIndex = null;
                        break;
                    }

                case NodeType.Extension:
                    {
                        visitor.VisitExtension(this, trieVisitContext);
                        trieVisitContext.AddVisited();
                        TrieNode child = GetChild(nodeResolver, path, 0);
                        if (child is null)
                        {
                            throw new InvalidDataException($"Child of an extension {Key} should not be null.");
                        }

                        child.ResolveKey(nodeResolver, GetChildPath(path, 0), false);
                        if (visitor.ShouldVisit(child.Keccak!))
                        {
                            trieVisitContext.Level++;
                            trieVisitContext.BranchChildIndex = null;
                            child.Accept(visitor, nodeResolver, GetChildPath(path, 0), trieVisitContext);
                            trieVisitContext.Level--;
                        }

                        break;
                    }

                case NodeType.Leaf:
                    {
                        visitor.VisitLeaf(this, trieVisitContext, Value.ToArray());
                        trieVisitContext.AddVisited();
                        if (!trieVisitContext.IsStorage && trieVisitContext.ExpectAccounts) // can combine these conditions
                        {
                            Account account = _accountDecoder.Decode(Value.AsRlpStream());
                            if (account.HasCode && visitor.ShouldVisit(account.CodeHash))
                            {
                                trieVisitContext.Level++;
                                trieVisitContext.BranchChildIndex = null;
                                visitor.VisitCode(account.CodeHash, trieVisitContext);
                                trieVisitContext.Level--;
                            }

                            if (account.HasStorage && visitor.ShouldVisit(account.StorageRoot))
                            {
                                trieVisitContext.IsStorage = true;
                                trieVisitContext.Level++;
                                trieVisitContext.BranchChildIndex = null;

                                if (TryResolveStorageRoot(nodeResolver, path, out TrieNode? storageRoot))
                                {
                                    TreePath storageAddress = path.Append(Key);
                                    storageRoot!.Accept(visitor, nodeResolver.GetStorageTrieNodeResolver(storageAddress.Path.ToCommitment()), TreePath.Empty, trieVisitContext);
                                }
                                else
                                {
                                    visitor.VisitMissingNode(account.StorageRoot, trieVisitContext);
                                }

                                trieVisitContext.Level--;
                                trieVisitContext.IsStorage = false;
                            }
                        }

                        break;
                    }

                default:
                    throw new TrieException($"An attempt was made to visit a node {Keccak} of type {NodeType}");
            }
        }
    }
}
