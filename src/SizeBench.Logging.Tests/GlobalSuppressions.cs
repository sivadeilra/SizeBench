﻿// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task",
                           Justification = "ConfigureAwait default is correct for app code, and thus seems good for test code too, see this blog post by Stephen Toub: https://devblogs.microsoft.com/dotnet/configureawait-faq/")]

[assembly: SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "This isn't important for test code.")]

[assembly: SuppressMessage("Usage", "CA2201:Do not raise reserved exception types", Justification = "Not important for test code.")]

[assembly: SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Performance of the tests isn't *that* important.")]
