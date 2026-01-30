# High-Performance External Merge Sort

A highly optimized, asynchronous .NET 8 solution for sorting massive text files (100GB+) that exceed available RAM.

## Overview

This project implements an **External Merge Sort** algorithm designed to process large datasets efficiently. It leverages modern C# features to minimize memory footprint and maximize throughput on both HDD and SSD storage.

The input file format is expected to be:
```text
415. Apple
30432. Something something something
1. Apple
32. Cherry is the best
2. Banana is yellow
```

**Sorting Criteria:**
1.  **String part** (Alphabetical/Ordinal).
2.  **Number part** (Ascending numeric) if strings are identical.

## Architecture

The solution is split into three main components:

* **`FileSorter`**: The core sorting engine.
    * **Phase 1 (Split):** Reads the input file, splits it into sorted chunks using a Producer-Consumer pipeline (`System.Threading.Channels`). Uses parallel Quicksort for in-memory processing.
    * **Phase 2 (Merge):** Merges sorted chunks using a K-Way Merge algorithm backed by a `PriorityQueue`. Supports multi-pass merging for extreme file sizes.
* **`TestFileGenerator`**: A multi-threaded utility to generate massive test datasets populated with random data to verify performance.
* **`Shared`**: Common logic, including zero-allocation parsing structs (`Row`) and centralized logging.

## Performance Features

* **Zero-Allocation Parsing:** Uses `ReadOnlySpan<char>` and custom structs to avoid creating millions of temporary strings during the comparison phase, significantly reducing Garbage Collector (GC) pressure.
* **Asynchronous Pipeline:** Implements a fully asynchronous pipeline using `System.Threading.Channels` to balance reading, sorting, and writing operations.
* **I/O Optimization:**
    * Tunable buffer sizes (default 4MB/16MB) to minimize disk syscalls.
    * `FileOptions.SequentialScan` hints for OS file caching.
    * Configurable concurrency limits (Semaphore) to prevent disk thrashing on HDDs.
* **Memory Efficiency:** Strict memory bounding ensures the application never exceeds the designated RAM limit, regardless of input file size.

## Getting Started

### Prerequisites
* .NET 8.0 SDK

### Build
```bash
dotnet build -c Release
```

### Usage

#### 1. Generate Test Data
Generate a 10GB file using all available CPU cores:
```bash
dotnet run --project TestFileGenerator --configuration Release -- --output data.txt --size 10 --cores 12
```

#### 2. Sort the File
Sort the generated file using 200MB memory chunks:
```bash
dotnet run --project FileSorter --configuration Release -- --input data.txt --output result.txt --chunk-size 200 --hdd-mode true --cores 12 --channels 4
```

### CLI Arguments

**FileSorter:**
* `--input`: Path to the source file.
* `--output`: Path to the destination file.
* `--chunk-size`: Memory limit per chunk in MB (default: 200).
* `--hdd-mode`: `true` to limit concurrent I/O (optimized for HDD), `false` for SSDs (default: true).
* `--temp`: Path to the temporary directory for chunks.
* `--cores`: Number of sorter threads.
* `--channels`: Channel limit for buffer

**TestFileGenerator:**
* `--output`: Path to the generated file.
* `--size`: Target size in GB (e.g., 10.5).
* `--cores`: Number of producer threads.

## Benchmarks

**Hardware Environment:**
* **CPU:** Intel Core i7-10700
* **RAM:** 64GB DDR4 3600MHz
* **Storage:** Seagate Barracuda 2TB (ST2000DM008) — *HDD SATA 7200RPM*

**Target:** 10 GB Text File generated using `TestFileGenerator`.

| Configuration | Chunk Size | Peak RAM | Duration | Avg Throughput |
| :--- | :--- | :--- | :--- | :--- |
| **Low RAM** | 50 MB | 1.4 GB | 08m 05s | **21 MB/s** |
| **Balanced** | 200 MB | ~3.2 GB | 05m 44s | **30 MB/s** |

> **Performance Note:**
> External Merge Sort requires reading and writing the entire dataset twice (Split Phase + Merge Phase).
> Therefore, an average processing speed of **30 MB/s** corresponds to approximately **120 MB/s** of continuous physical Disk I/O (Read+Write), effectively saturating the sequential bandwidth of the mechanical HDD.
> Cold vs. Hot Run: The "Balanced" result reflects a "Hot" run where the OS utilizes available RAM (64GB) to cache the input file. A "Cold" run (pure HDD read access) typically aligns closer to the "Low RAM" timing (**~8 minutes**).

## Testing

The solution includes a comprehensive test suite using **NUnit**:
* **Unit Tests:** Verify parsing logic (`RowTests`), stream handling (`ChunkStreamTests`).
* **Integration Tests:** Verify split/merge logic and data integrity (`ParallelSplitterTests`, `MultiPassMergerTests`).

Run tests:
```bash
dotnet test
```