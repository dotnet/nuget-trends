Unfortunately this code was not made available via NuGet.
For that reason it's being copied over.

The original location is a sample project in the NuGet org:

https://github.com/NuGet/Samples/tree/875f320e43420932c1d8d554e116f6687ac1964f/CatalogReaderExample/NuGet.Protocol.Catalog

Modifications:

Removed the bool return value and let exceptions bubble up.
Use of C# 7  features to get rid of the IDE 'messages'
Added cancellation token support
Running multiple http requests concurrently
batching by 25
on failed deserialization, log and return default
if leaf type is unknown, log and continue
retargeted to netstandard2.0
added log scopes

Check files history for details
