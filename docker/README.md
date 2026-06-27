# Toloka-enhanced Jackett — Docker

A drop-in replacement for the official LinuxServer Jackett image, identical except
for the enhanced **Toloka.to** indexer built from the `feature/toloka-enhancements`
branch. It is an *overlay*: only `Jackett.Common.dll` is rebuilt and swapped onto
`lscr.io/linuxserver/jackett`, so your existing config, ports and volumes all work
unchanged.

## Use a published image (recommended)

Images are published to GHCR by the **Docker (Toloka)** GitHub Action on every push
to the branch:

```
ghcr.io/maksii/jackett-toloka:latest
```

> After the first CI run, make the package **Public** in
> GitHub → your profile → Packages → `jackett-toloka` → Package settings,
> so others can `docker pull` without a token.

Run it:

```bash
docker compose -f docker/docker-compose.yml up -d
# http://localhost:9117  →  add the "Toloka" indexer
```

or plain `docker run`:

```bash
docker run -d --name jackett-toloka \
  -e PUID=1000 -e PGID=1000 -e TZ=Etc/UTC \
  -p 9117:9117 \
  -v ./config:/config \
  ghcr.io/maksii/jackett-toloka:latest
```

## Build locally

```bash
# from the repo root (build context must be the root)
docker build -f docker/Dockerfile.toloka -t jackett-toloka .
docker run -d -p 9117:9117 -v ./config:/config jackett-toloka
```

No local .NET SDK needed — the build runs entirely inside the container.

## How it stays in sync with upstream

`Dockerfile.toloka` pins `BASE_TAG` to the upstream Jackett version this branch is
based on (currently **0.24.2125**). When you rebase the branch onto a newer Jackett,
bump `BASE_TAG` to the matching [LinuxServer tag](https://hub.docker.com/r/linuxserver/jackett/tags)
so the swapped DLL stays ABI-compatible with the rest of the image.

> **Do not enable Jackett/LSIO auto-update** for this container — an in-place update
> would re-download stock Jackett and overwrite the Toloka DLL. Update by pulling a
> new image instead.
