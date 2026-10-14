using System.Runtime.CompilerServices;

// Internal members (for example LoginAttemptThrottle.TrackedAccountCount) are visible to the
// test project so the memory bound can be asserted without widening the public API.
[assembly: InternalsVisibleTo("SmartSchoolAPI.Tests")]
