# TODO

- [x] Decide whether the Dockerfile should include the sample plugins
- [x] Check whether the docker action can be optimized/cached
- [x] Generate release notes for the GitHub release from CHANGELOG.md
- [x] Allow passing regex options to the OnRegex attribute
- [ ] Provide a way for handler filters to pass information on to the underlying handler (i.e. authentication info)
- [x] Consider moving IrcBot.BatchChannels to a utility class, so that plugins can reuse it
- [x] Consider making CommandContext, RegexContext, etc share a common HandlerContext
- [x] Potentially remove PluginType from HandlerGroup
- [x] Switch the JSON config parser to one that supports JSON5
- [x] Check whether the extra `AppContext.BaseDirectory` logic is actually required for dependency resolution with a `PublishSingleFile` assembly
- [x] Check whether we can optimize the docker action further using cache mounts
- [ ] Add integration tests for plugin loading