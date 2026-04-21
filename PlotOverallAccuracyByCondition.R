suppressPackageStartupMessages(library(ggplot2))

# ----------------------------- USER SETTINGS ----------------------------------
DATA_ROOT <- "/Users/makenzygilbert/OpenDynamicsUnity/seed1702_inputs"
SEED <- 1702
PASS_THRESHOLD <- 90.5

# If TRUE, only count patternIds that exist at the final stage (prevents inflated denominators).
FILTER_TO_FINAL_PATTERN_SET <- TRUE

# Which stimulus families + conditions to plot
STIMS <- c("radials", "geometric20")
CONDS <- c("neutral", "condA", "condB", "condC")
# ------------------------------------------------------------------------------

build_condition_names <- function(seed = SEED, stims = STIMS, conds = CONDS) {
  as.vector(unlist(lapply(stims, function(st) paste0(st, "_", conds, "_seed", seed))))
}

read_recall_history <- function(folder) {
  f <- file.path(folder, "recall_history.csv")
  if (!file.exists(f)) {
    stop("recall_history.csv not found in:\n  ", folder)
  }
  d <- read.csv(f, stringsAsFactors = FALSE)
  req <- c("patternId", "stage", "recallPercent")
  if (!all(req %in% names(d))) {
    stop("recall_history.csv is missing required columns: ", paste(req, collapse = ", "))
  }
  d$patternId <- as.character(d$patternId)
  d$stage <- as.integer(d$stage)
  d$recallPercent <- as.numeric(d$recallPercent)
  d
}

compute_overall_accuracy_from_recalls <- function(
    recall_data,
    pass_threshold = PASS_THRESHOLD,
    filter_to_final_pattern_set = FILTER_TO_FINAL_PATTERN_SET) {
  if (is.null(recall_data) || nrow(recall_data) == 0L) {
    stop("recall_data is empty.")
  }
  all_stages <- sort(unique(recall_data$stage))
  final_stage <- max(all_stages)
  final_ids <- unique(recall_data$patternId[recall_data$stage == final_stage])

  out <- data.frame(
    stage = integer(),
    passedPatterns = integer(),
    totalPatterns = integer(),
    accuracyPercent = numeric(),
    stringsAsFactors = FALSE
  )

  for (st in all_stages) {
    sd <- recall_data[recall_data$stage == st, , drop = FALSE]
    if (isTRUE(filter_to_final_pattern_set)) {
      sd <- sd[sd$patternId %in% final_ids, , drop = FALSE]
    }
    if (nrow(sd) == 0L) {
      next
    }
    pats <- unique(sd$patternId)
    last_recall <- sapply(pats, function(pid) tail(sd$recallPercent[sd$patternId == pid], 1))
    total <- length(last_recall)
    passed <- sum(last_recall >= pass_threshold)
    out <- rbind(
      out,
      data.frame(
        stage = st,
        passedPatterns = passed,
        totalPatterns = total,
        accuracyPercent = if (total > 0L) (passed / total) * 100 else NA_real_,
        stringsAsFactors = FALSE
      )
    )
  }
  out
}

load_all_conditions <- function(
    data_root = DATA_ROOT,
    seed = SEED,
    stims = STIMS,
    conds = CONDS,
    pass_threshold = PASS_THRESHOLD,
    filter_to_final_pattern_set = FILTER_TO_FINAL_PATTERN_SET,
    stop_on_missing = FALSE) {
  cond_names <- build_condition_names(seed = seed, stims = stims, conds = conds)
  all_df <- data.frame()

  for (cond in cond_names) {
    folder <- file.path(data_root, cond)
    res <- tryCatch(
      {
        rh <- read_recall_history(folder)
        acc <- compute_overall_accuracy_from_recalls(
          rh,
          pass_threshold = pass_threshold,
          filter_to_final_pattern_set = filter_to_final_pattern_set
        )
        acc$condition <- cond
        acc
      },
      error = function(e) {
        msg <- paste0("Skipping ", cond, ": ", conditionMessage(e))
        message(msg)
        if (isTRUE(stop_on_missing)) {
          stop(msg, call. = FALSE)
        }
        NULL
      }
    )
    if (!is.null(res)) {
      all_df <- rbind(all_df, res)
    }
  }

  if (nrow(all_df) == 0L) {
    stop("No conditions loaded. Check DATA_ROOT and folder names.", call. = FALSE)
  }

  all_df$label <- gsub(paste0("_seed", seed, "$"), "", all_df$condition)
  all_df$stimulus <- sub("^(radials|geometric20)_.*$", "\\1", all_df$label)
  all_df$cond <- sub("^(radials|geometric20)_", "", all_df$label)
  all_df
}

plot_overall_accuracy_conditions <- function(all_df, stimulus = NULL) {
  all_df <- all_df[is.finite(all_df$accuracyPercent), , drop = FALSE]
  if (!is.null(stimulus)) {
    stimulus <- as.character(stimulus)[1L]
    all_df <- all_df[all_df$stimulus == stimulus, , drop = FALSE]
  }
  ggplot(all_df, aes(stage, accuracyPercent, color = label)) +
    geom_line(linewidth = 1.2) +
    geom_point(size = 3) +
    geom_text(
      aes(label = paste0(passedPatterns, "/", totalPatterns)),
      vjust = -1.0,
      size = 3.5,
      color = "gray40",
      show.legend = FALSE
    ) +
    scale_x_continuous(breaks = sort(unique(all_df$stage))) +
    coord_cartesian(ylim = c(0, 100)) +
    labs(
      title = if (is.null(stimulus)) {
        "Recall Accuracy Declines with Increasing Memory Load"
      } else {
        paste0("Recall Accuracy Declines with Increasing Memory Load (", stimulus, ")")
      },
      subtitle = paste0(
        "Pass threshold ≥", PASS_THRESHOLD, "%  |  ",
        "Filter-to-final-set: ", FILTER_TO_FINAL_PATTERN_SET
      ),
      x = "Number of Patterns Learned (Stage)",
      y = "Recall Accuracy (%)",
      color = "Condition"
    ) +
    theme_minimal(base_size = 12) +
    theme(
      text = element_text(face = "bold"),
      plot.title = element_text(face = "bold"),
      plot.subtitle = element_text(face = "bold"),
      axis.title = element_text(face = "bold"),
      axis.text = element_text(face = "bold"),
      legend.title = element_text(face = "bold"),
      legend.text = element_text(face = "bold"),
      legend.position = "left"
    )
}

# ---- Run (in RStudio: Source this file) ----
all_df <- load_all_conditions()
for (stim in sort(unique(all_df$stimulus))) {
  p <- plot_overall_accuracy_conditions(all_df, stimulus = stim)
  print(p)
}

# Optional: save next to this script's working directory
# ggsave("overall_accuracy_by_condition.png", plot = p, width = 10, height = 5.8, dpi = 300)
