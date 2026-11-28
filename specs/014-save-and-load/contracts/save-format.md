# Contract: `.talt` save file format, container version 1

All integers little-endian. Offsets are from the start of the file. "Checksum" is XxHash3-64 of the
bytes named (seed 0). Readers MUST verify every checksum before constructing any state (FR-019).

## Header (64 bytes, offset 0)

| Offset | Size | Field | Value / meaning |
|---|---|---|---|
| 0 | 8 | Magic | ASCII `TALTSAVE` |
| 8 | 2 | ContainerVersion | `1` |
| 10 | 2 | Flags | 0 (reserved, must be 0 in version 1) |
| 12 | 4 | HeaderLength | `64` |
| 16 | 8 | DetailsOffset | start of the details block |
| 24 | 4 | DetailsLength | bytes |
| 28 | 8 | ThumbnailOffset | start of the thumbnail block (0 when absent) |
| 36 | 4 | ThumbnailLength | bytes |
| 40 | 8 | TableOffset | start of the section table |
| 48 | 4 | TableLength | bytes |
| 52 | 4 | Reserved | 0 |
| 56 | 8 | HeaderChecksum | checksum of bytes 0–55 |

A file shorter than 64 bytes, with another magic, or with a bad header checksum is `NotASave` or
`Damaged`. A `ContainerVersion` above the reader's is `TooNew`.

## Details block

`[u32 jsonLength][jsonLength bytes UTF-8 JSON][u64 checksum of the JSON bytes]`

JSON object with the `SaveDetails` fields of [data-model.md](../data-model.md) in camelCase. Unknown
properties are ignored (details may gain fields without a container change). Maximum 64 KiB.

## Thumbnail block

`[u16 width][u16 height][u8 codec][u32 compressedLength][compressed RGB24 pixels][u64 checksum of the compressed bytes]`

Version 1 writes 256 × 256 with codec 1 (Deflate).

## Section data

Sections follow the thumbnail, in the order of the table. Each section is a sequence of blocks; each
block is the compressed form of at most 4 MiB (4 194 304 bytes) of the section's uncompressed payload.
Blocks of one section are contiguous; the uncompressed payload of the section is the concatenation of
its blocks' uncompressed bytes.

Codecs: `0` stored, `1` Deflate (raw, RFC 1951), `2` Brotli. Unknown codec → `TooNew`.

## Section table

```
u32 sectionCount
repeat sectionCount:
    u32  id                (four ASCII characters, first character in the low byte)
    u16  sectionVersion
    u16  reserved (0)
    u64  uncompressedLength
    u32  blockCount
    repeat blockCount:
        u64 offset
        u32 compressedLength
        u32 uncompressedLength (≤ 4 MiB; every block but the last of a section is exactly 4 MiB)
        u8  codec
        u8  reserved[3]
        u64 checksum of the compressed bytes
u64 checksum of everything above in the table
```

Rules for readers:

- Each id appears at most once; a duplicate is `Damaged`.
- The sum of a section's block uncompressed lengths equals its `uncompressedLength`.
- Blocks must lie inside the file and must not overlap the header, details, thumbnail or table.
- An id the reader does not know is `TooNew` (FR-021). A `sectionVersion` above the reader's version
  for that id is `TooNew`.
- Section readers must consume exactly `uncompressedLength` bytes; leftover or missing bytes are
  `Damaged`.

## Writing rules

- Write to `<file>.tmp` in the same folder, flush (`FileStream.Flush(flushToDisk: true)`), then
  `File.Move(tmp, file, overwrite: true)`. A failure deletes the temporary file and leaves `<file>`
  as it was (FR-007).
- Sections are written in restore order (the table order is the order readers restore them in).

## Section payloads

Each section's payload is defined by its owner as a sequence of `SaveWriter` primitives
([data-model.md](../data-model.md)). The payload layout of version 1 of each section is documented in
the XML comments of its `Write` method; any change to it requires a section version bump
([save-sections.md](save-sections.md)).
