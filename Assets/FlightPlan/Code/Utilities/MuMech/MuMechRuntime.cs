using KSP.Game;
using KSP.Sim.impl;

namespace MuMech
{
    /// <summary>
    /// Call-time accessors for the live game instance, for the vendored MuMech layer.
    ///
    /// NodeManager captured these in a type-load-time static initializer - a `static readonly`
    /// field of type <c>GameInstance</c>, assigned from <c>GameManager.Instance</c>, at
    /// <c>OrbitExtensions.cs:25</c> and <c>MuUtils.cs:20</c>. A type-load-time read of
    /// <c>GameManager.Instance</c> throws if the type is first touched before the game
    /// instance exists - and on 0.2.8.5 the loader touches plugin types during registration, which
    /// is exactly that window. These properties are therefore evaluated on every call, never
    /// captured; that is the whole reason this type exists. (The literal removed line is recorded
    /// in <c>Deploy/obj/PORT-PROGRESS.md</c>, P2 block.)
    ///
    /// The route itself is the one the live in-repo 0.2.8.5 mods use:
    /// <c>mods/MicroEngineer/.../Code/Utilities/Utility.cs:38</c> -
    /// <c>GameManager.Instance.Game.UniverseModel.UniverseTime</c>.
    /// </summary>
    internal static class MuMechRuntime
    {
        /// <summary>The current <see cref="GameInstance"/>. Never cached across calls.</summary>
        internal static GameInstance Game => GameManager.Instance.Game;

        /// <summary>The live universe model. Never cached across calls.</summary>
        internal static UniverseModel UniverseModel => Game.UniverseModel;

        /// <summary>
        /// Current universal time. Never cached across calls - this is the value the legacy code
        /// read from its captured <c>Game</c> field, so the callers' behaviour is unchanged except
        /// that the read now happens at call time rather than at type load.
        /// </summary>
        internal static double UniverseTime => Game.UniverseModel.UniverseTime;
    }
}
