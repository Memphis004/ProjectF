using System;
using Bencodex.Types;
using Libplanet.Action;
using Libplanet.Action.State;
using Libplanet.Crypto;
using ProjectF.Lib.Exceptions;

namespace ProjectF.Lib.Actions;

/// <summary>
/// Base class for all ProjectF on-chain actions.
///
/// Wire format is a Bencodex envelope (knowledge.md rule 5):
/// <code>
/// {
///   "type_id": "action_name_v1",
///   "values": { ...action specific payload... }
/// }
/// </code>
/// </summary>
public abstract class ActionBase : IAction
{
    /// <summary>Stable action identifier embedded in the envelope.</summary>
    public abstract string TypeId { get; }

    /// <inheritdoc cref="IAction.PlainValue"/>
    public IValue PlainValue => new Dictionary(new Dictionary<IKey, IValue>
    {
        [(Text)"type_id"] = (Text)TypeId,
        [(Text)"values"] = EncodePayload(),
    });

    /// <inheritdoc cref="IAction.LoadPlainValue"/>
    public void LoadPlainValue(IValue plainValue)
    {
        if (plainValue is not Dictionary envelope
            || !envelope.TryGetValue((Text)"type_id", out var typeIdValue)
            || typeIdValue is not Text typeId
            || !envelope.TryGetValue((Text)"values", out var payloadValue))
        {
            throw new FailedLoadStateException(
                "Malformed action envelope: expected a Bencodex Dictionary with " +
                "\"type_id\" (Text) and \"values\" (IValue).");
        }

        if (typeId.Value != TypeId)
        {
            throw new FailedLoadStateException(
                $"type_id mismatch: expected \"{TypeId}\", got \"{typeId.Value}\".");
        }

        DecodePayload(payloadValue);
    }

    /// <inheritdoc cref="IAction.Execute"/>
    public IWorld Execute(IActionContext context)
    {
        // knowledge.md rule 2: Execute() must be 100% deterministic — no wall
        // clock, no System.Random, no I/O. Only context.BlockIndex,
        // context.GetRandom() and the action's decoded payload may influence
        // the result. Libplanet re-executes actions on every node.
        return ExecuteInternal(context);
    }

    /// <summary>Serializes the action-specific payload (the "values" part).</summary>
    protected abstract IValue EncodePayload();

    /// <summary>Restores action fields from the "values" payload.</summary>
    protected abstract void DecodePayload(IValue payload);

    /// <summary>The deterministic game logic, executed on every full node.</summary>
    protected abstract IWorld ExecuteInternal(IActionContext context);

    /// <summary>
    /// Returns the mutable account for <paramref name="accountSpace"/>,
    /// creating an empty one if the space was never written before.
    /// </summary>
    /// <remarks>
    /// Libplanet 5.x world model: <c>world.GetAccount(space)</c> returns null
    /// for never-touched spaces; <c>world.GetAccountState(space)</c> is the
    /// read-only view used as the base of a fresh mutable <c>Account</c>.
    /// The stage-1 pipeline checkpoint (Unity signs PingAction end to end)
    /// validates this against the real 5.5.3 package before any gameplay
    /// action depends on it.
    /// </remarks>
    protected static IAccount GetOrCreateAccount(IWorld world, Address accountSpace)
    {
        IAccount? account = world.GetAccount(accountSpace);
        if (account is not null)
        {
            return account;
        }

        IAccountState? accountState = world.GetAccountState(accountSpace);
        if (accountState is not null)
        {
            return new Account(accountState);
        }

        throw new FailedLoadStateException(
            $"Account space {accountSpace} does not exist in this world.");
    }

    /// <summary>
    /// knowledge.md rule 6: every action mutating a player's state must verify
    /// the transaction signer owns the avatar before anything else.
    /// </summary>
    protected static void EnsureOwner(IActionContext context, Address avatarAddress)
    {
        if (context.Signer != avatarAddress)
        {
            // TODO(stage-3): dedicated PermissionDeniedException once the
            // action set lands; InvalidOperationException is enough for the
            // pipeline-proof checkpoint.
            throw new InvalidOperationException(
                $"Signer {context.Signer} is not the owner of avatar {avatarAddress}.");
        }
    }
}
