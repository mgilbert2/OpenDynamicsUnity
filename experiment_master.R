# ==============================================================================
# Master experiment queue for Unity CSVExperimentRunner (batch JSON)
# ==============================================================================
# Design: encode every run here with defaults + overrides so the full design
# is visible in one place. R writes Assets/StreamingAssets/experiment_batch.json;
# Unity runs all enabled entries in order when CSVExperimentRunner.batchFileName
# is set (e.g. "experiment_batch.json").
#
# Optional: install.packages("jsonlite") for prettier JSON. If jsonlite is missing or
# broken (Windows DLL "Permission denied"), write_experiment_batch() uses base R only.
#
# Defaults match Unity scene "StimBCondition B" (randomSeed 1702) in Attractorscopy(noAI).unity.
# Scene listed test_08 but waypoint_test_patterns_10.csv only has test_01..07 — defaults use seven IDs.
#
# Workflow:
#   setwd("C:/Users/Mak/Attractors")
#   source("experiment_master.R")
#   clear_experiment_queue()
#   run_experiment(stim_set = "radials", experiment_name = "E1_baseline")  # uses all defaults + stim
#   run_experiment(stim_set = "radials", experiment_name = "E2_magnet_stays_on", magnet_on = TRUE)
#   write_experiment_batch()
#
# In Unity: assign batchFileName = experiment_batch.json on CSVExperimentRunner,
# clear or disable Inspector experiments (or leave them; batch takes priority if file exists).
# ==============================================================================

# Stimulus presets: CSV file under StreamingAssets + pattern IDs (empty = all patterns in file)
STIM_SETS <- list(
  radials = list(
    csv = "waypoint_test_patterns_10.csv",
    patterns = sprintf("test_%02d", 1:7)
  ),
  # "angles" / geometric set used in repo (IDs are geo_01 .. geo_20)
  geometric20 = list(
    csv = "waypoint_geometric_20.csv",
    patterns = sprintf("geo_%02d", 1:20)
  ),
  angles = list(
    csv = "waypoint_geometric_20.csv",
    patterns = sprintf("geo_%02d", 1:20)
  )
)

#' Default values for one batch entry (camelCase = Unity / JsonUtility field names).
#' Synced with CSVExperimentConfig defaults (StimB / seed 1702 scene baseline).
default_experiment_entry <- function() {
  list(
    enabled = TRUE,
    experimentName = "CSV_Experiment1",
    randomSeed = 1702L,
    stimulusCsvFileName = "",
    patternIdsToRun = sprintf("test_%02d", 1:7),
    runRecallAfterEachPattern = FALSE,
    cumulativeRecallMode = TRUE,
    randomizeRecallOrder = FALSE,
    resetStabilizationTime = 0,
    noiseDelayAfterTraining = 0,
    recallNoiseTarget = 1L, # 0=Ball, 1=Magnet, 2=Both, 3=None
    noiseStrength = 100,
    whiteNoise = FALSE,
    noiseSmoothing = 0,
    magnetNoiseStrength = 100,
    magnetNoiseWhite = FALSE,
    magnetNoiseSmoothing = 0.04,
    magnetNoiseMeanReversion = 2.57,
    enableCueFadeSystem = TRUE,
    neverTurnOffMagnetDuringRecall = FALSE,
    cueOffAtProgress = 0.05,
    recallMagnetForceMultiplier = 1,
    recallRadiusThreshold = 1.5,
    recallRequiredPercent = 90.5,
    recallSampleInterval = 0.5,
    trainingPassesPerPattern = 1L,
    delayBetweenTrainingPasses = 0,
    maxWellDepth = 1,
    normalizeDepth = FALSE,
    normalizedDepthTarget = 1,
    hypoWidth = 0.2,
    wellMergeDistance = 0.343,
    ballDamping = 2.55,
    ballMaxSpeed = 6.3,
    ballVelocityMultiplier = 0.62,
    landscapeGain = 4.05,
    externalGain = 57.4
  )
}

.batch_queue <- new.env(parent = emptyenv())
.batch_queue$items <- list()

clear_experiment_queue <- function() {
  .batch_queue$items <- list()
  invisible(TRUE)
}

#' Normalize friendly R names into Unity JSON names; merge onto defaults.
.normalize_run_args <- function(over) {
  if (!is.null(over$experiment_name)) {
    over$experimentName <- over$experiment_name
    over$experiment_name <- NULL
  }
  if (!is.null(over$seed)) {
    over$randomSeed <- as.integer(over$seed)
    over$seed <- NULL
  }
  if (!is.null(over$magnet_on)) {
    # magnet_on TRUE = keep cue on (no fade to zero); matches neverTurnOffMagnetDuringRecall
    over$neverTurnOffMagnetDuringRecall <- as.logical(over$magnet_on)
    over$magnet_on <- NULL
  }
  if (!is.null(over$magnet_noise)) {
    over$magnetNoiseStrength <- as.numeric(over$magnet_noise)
    over$magnet_noise <- NULL
  }
  if (!is.null(over$training_passes)) {
    over$trainingPassesPerPattern <- as.integer(over$training_passes)
    over$training_passes <- NULL
  }
  if (!is.null(over$recall_required_percent)) {
    over$recallRequiredPercent <- as.numeric(over$recall_required_percent)
    over$recall_required_percent <- NULL
  }
  over
}

#' Queue one experiment. Pass only fields that differ from default_experiment_entry().
#' Use stim_set = "radials" | "geometric20" | "angles" to set CSV + pattern list.
run_experiment <- function(..., stim_set = NULL) {
  over <- list(...)
  over <- .normalize_run_args(over)

  if (!is.null(stim_set)) {
    if (!stim_set %in% names(STIM_SETS)) {
      stop("Unknown stim_set: ", stim_set, ". Use: ", paste(names(STIM_SETS), collapse = ", "))
    }
    ss <- STIM_SETS[[stim_set]]
    over <- modifyList(
      list(stimulusCsvFileName = ss$csv, patternIdsToRun = ss$patterns),
      over
    )
  }

  base <- default_experiment_entry()
  merged <- modifyList(base, over)
  n <- length(.batch_queue$items) + 1L
  .batch_queue$items[[n]] <- merged
  invisible(merged)
}

# --- Base R JSON (Unity JsonUtility-compatible) when jsonlite unavailable ------------

.json_esc <- function(s) {
  s <- as.character(s)[1L]
  s <- gsub("\\", "\\\\", s, fixed = TRUE)
  s <- gsub("\"", "\\\"", s, fixed = TRUE)
  s <- gsub("\r", "\\r", s, fixed = TRUE)
  s <- gsub("\n", "\\n", s, fixed = TRUE)
  s <- gsub("\t", "\\t", s, fixed = TRUE)
  paste0("\"", s, "\"")
}

.json_value <- function(v) {
  if (is.null(v)) {
    return("null")
  }
  if (is.logical(v) && length(v) == 1L) {
    return(if (isTRUE(v)) "true" else "false")
  }
  if (is.integer(v) && length(v) == 1L) {
    return(as.character(v))
  }
  if (is.numeric(v) && length(v) == 1L) {
    return(sprintf("%.10g", as.double(v)))
  }
  if (is.character(v)) {
    if (length(v) == 0L) {
      return("[]")
    }
    if (length(v) == 1L) {
      return(.json_esc(v))
    }
    return(paste0("[", paste(vapply(v, .json_esc, ""), collapse = ", "), "]"))
  }
  stop("Unsupported type for JSON: ", typeof(v), " (length ", length(v), ")")
}

.write_experiment_batch_base <- function(path, items) {
  keys <- names(default_experiment_entry())
  n <- length(items)
  parts <- c("{\n", '  "entries": ')
  if (n == 0L) {
    parts <- c(parts, "[]\n}\n")
    writeLines(paste(parts, collapse = ""), path)
    return(invisible(TRUE))
  }
  parts <- c(parts, "[\n")
  for (i in seq_len(n)) {
    e <- items[[i]]
    pairs <- vapply(keys, function(k) {
      paste0("      \"", k, "\": ", .json_value(e[[k]]))
    }, "")
    block <- paste0(
      "    {\n",
      paste(pairs, collapse = ",\n"),
      "\n    }"
    )
    if (i < n) {
      block <- paste0(block, ",")
    }
    parts <- c(parts, block, "\n")
  }
  parts <- c(parts, "  ]\n}\n")
  writeLines(paste(parts, collapse = ""), path)
  invisible(TRUE)
}

#' Write batch JSON for Unity. Default path: ./Assets/StreamingAssets/experiment_batch.json
#' Uses jsonlite when available; otherwise writes the same structure with base R only.
write_experiment_batch <- function(path = NULL) {
  if (is.null(path)) {
    path <- file.path(getwd(), "Assets", "StreamingAssets", "experiment_batch.json")
  }
  dir.create(dirname(path), recursive = TRUE, showWarnings = FALSE)
  items <- .batch_queue$items
  used <- "jsonlite"
  if (requireNamespace("jsonlite", quietly = TRUE)) {
    tryCatch(
      jsonlite::write_json(
        list(entries = items),
        path,
        pretty = TRUE,
        auto_unbox = TRUE
      ),
      error = function(e) {
        message(
          "jsonlite::write_json failed (",
          conditionMessage(e),
          "); using base R JSON writer."
        )
        used <<- "base R"
        .write_experiment_batch_base(path, items)
      }
    )
  } else {
    used <- "base R"
    message("Package jsonlite not installed; using base R JSON writer (no extra packages).")
    .write_experiment_batch_base(path, items)
  }
  message(
    "Wrote ", length(items), " experiment(s) via ", used, " to:\n  ",
    normalizePath(path, winslash = "/", mustWork = FALSE)
  )
  invisible(path)
}

# ==============================================================================
# Example design (comment out or delete; or duplicate and edit)
# ==============================================================================
.example_run <- function() {
  clear_experiment_queue()

  # 1 — full default params + radial stim (matches StimB / seed 1702 baseline)
  run_experiment(
    stim_set = "radials",
    experiment_name = "E1_radials_baseline_1702"
  )

  # 2 — same, but magnet cue does not fade (neverTurnOffMagnetDuringRecall = TRUE)
  run_experiment(
    stim_set = "radials",
    experiment_name = "E2_radials_magnet_stays_on",
    magnet_on = TRUE
  )

  # 3 — geometric stimuli, extra training pass
  run_experiment(
    stim_set = "angles",
    experiment_name = "E3_geo_2pass",
    training_passes = 2L
  )

  write_experiment_batch()
}

# Uncomment to auto-generate example batch when sourcing:
# .example_run()
