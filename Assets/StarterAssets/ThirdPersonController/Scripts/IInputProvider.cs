using UnityEngine;

namespace StarterAssets
{
    /// <summary>
    /// Input abstraction interface that decouples the ThirdPersonController from input sources.
    /// Both player input and AI input providers implement this interface.
    /// </summary>
    public interface IInputProvider
    {
        /// <summary>Movement direction as a normalized Vector2 (X, Z in world space)</summary>
        Vector2 move { get; }

        /// <summary>Look/camera direction input (typically mouse or right stick)</summary>
        Vector2 look { get; }

        /// <summary>Jump input flag</summary>
        bool jump { get; set; }

        /// <summary>Sprint input flag</summary>
        bool sprint { get; }

        /// <summary>Vault input flag</summary>
        bool vault { get; }

        /// <summary>Whether movement uses analog stick magnitude or binary movement</summary>
        bool analogMovement { get; }
    }
}