# TODO

- [x] Decide whether the Dockerfile should include the sample plugins
- [x] Check whether the docker action can be optimized/cached
- [x] Generate release notes for the GitHub release from CHANGELOG.md
- [ ] Allow passing regex options to the OnRegex attribute
- [ ] Provide a way for handler filters to pass information on to the underlying handler (i.e. authentication info)
- [ ] Consider moving IrcBot.BatchChannels to a utility class, so that plugins can reuse it
- [ ] Consider making CommandContext, RegexContext, etc share a common HandlerContext
- [ ] Potentially remove PluginType from HandlerGroup
- [ ] Switch the JSON config parser to one that supports JSON5
- [ ] Check whether the extra `AppContext.BaseDirectory` logic is actually required for dependency resolution with a `PublishSingleFile` assembly