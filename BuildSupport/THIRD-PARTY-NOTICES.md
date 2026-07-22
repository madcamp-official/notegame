# Ninja Adventure third-party notices

Ninja Adventure includes the following third-party assets and fonts. The full
license texts are shipped inside the macOS app at
`Contents/Resources/ThirdPartyLicenses/` and beside the Windows player in
`ThirdPartyLicenses/`.

## Ninja Adventure Asset Pack

- Creators: [Pixel-boy](https://pixel-boy.itch.io/) and
  [AAA](https://www.instagram.com/challenger.aaa/)
- Source: [Ninja Adventure Asset Pack](https://pixel-boy.itch.io/ninja-adventure-asset-pack)
- License: CC0 1.0 Universal (`LICENSE.txt`)

## NeoDunggeunmo Pro

- Copyright © 2017–2024 Eunbin Jeong (Dalgona.)
- Reserved Font Names: "Neo둥근모 Pro" and "NeoDunggeunmo Pro"
- License: SIL Open Font License 1.1 (`NeoDunggeunmoPro-LICENSE.txt`)

## Liberation Sans

- Digitized data copyright © 2010 Google Corporation
- Copyright © 2012 Red Hat, Inc.
- License: SIL Open Font License 1.1 (`LiberationSans - OFL.txt`)

## Server runtime packages

The macOS runtime archive installs the production-only packages pinned by
`Server/package-lock.json`. Each package's copyright and license material is
retained in its own `Server/node_modules/<package>/` directory.

- MIT: `pg` 8.22.0, `pg-cloudflare` 1.4.0, `pg-connection-string` 2.14.0,
  `pg-pool` 3.14.0, `pg-protocol` 1.15.0, `pg-types` 2.2.0, `pgpass` 1.0.5,
  `postgres-array` 2.0.0, `postgres-bytea` 1.0.1, `postgres-date` 1.0.7,
  `postgres-interval` 1.2.0, and `xtend` 4.0.2
- ISC: `pg-int8` 1.0.1 and `split2` 4.2.0

The release helper downloads the pinned official PostgreSQL 16 Alpine image
through Docker. The image is not embedded in the game archive; its upstream
license information is published with the official image at
<https://hub.docker.com/_/postgres>.
