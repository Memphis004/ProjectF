# Auto-Watch Test Note

This is a tiny test note created to verify that the karpathywiki plugin's
auto-watch feature ingests new files from `sources/` automatically.

## Facts for extraction

- **ProjectF** is a pixel-art fishing/farming game built on Libplanet 5.5.3.
- The **PingAction** is the first on-chain action of ProjectF; it increments a
  global ping counter stored in the Ping account space.
- The **SeedNode** is the bootstrap peer of the ProjectF dev network; it mines
  blocks every 2 seconds and serves genesis + peer info from its store folder.

If auto-watch works, this note should be ingested within seconds of being
saved, producing entity/concept pages under `wiki/` and a provenance page
under `wiki/sources/`.
