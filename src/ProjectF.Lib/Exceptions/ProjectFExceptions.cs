using System;

namespace ProjectF.Lib.Exceptions;

/// <summary>Avatar lacks the stamina an action requires.</summary>
public class NotEnoughStaminaException : Exception
{
    public NotEnoughStaminaException(string message)
        : base(message)
    {
    }
}

/// <summary>A required item is missing or insufficient in the inventory.</summary>
public class ItemNotFoundException : Exception
{
    public ItemNotFoundException(string message)
        : base(message)
    {
    }
}

/// <summary>All pond slots are taken (and none of them belong to the signer).</summary>
public class PondFullException : Exception
{
    public PondFullException(string message)
        : base(message)
    {
    }
}

/// <summary>Avatar lacks the gold an action requires.</summary>
public class NotEnoughGoldException : Exception
{
    public NotEnoughGoldException(string message)
        : base(message)
    {
    }
}

/// <summary>A state value could not be loaded or has an unexpected shape.</summary>
public class FailedLoadStateException : Exception
{
    public FailedLoadStateException(string message)
        : base(message)
    {
    }
}

/// <summary>The transaction signer does not own the avatar it tries to mutate.</summary>
public class PermissionDeniedException : Exception
{
    public PermissionDeniedException(string message)
        : base(message)
    {
    }
}
