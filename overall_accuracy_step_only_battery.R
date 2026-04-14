# ==============================================================================
# Full 2×4 battery: step_saturated ONLY (piecewise-constant by stage)
# ==============================================================================
# For every stim × condition cell with recall_history.csv, fits
#   lm(accuracyPercent ~ factor(stage))
# and plots that step curve (no linear / exp / power / sigmoid).
#
# Writes per run folder:
#   rgraphs/overall_accuracy_step_only/overall_accuracy_step_only.png
#   rgraphs/overall_accuracy_step_only/overall_accuracy_step_summary.csv
#
# Combined outputs (same aggregate parent convention as overall_accuracy_best_fit.R):
#   <aggregate_parent>/overall_accuracy_step_only_seed_<seed>/combined_step_only_summary.csv
#
# Usage:
#   setwd("C:/Users/Mak/Attractors")
#   source("overall_accuracy_step_only_battery.R")
#   run_overall_accuracy_step_only("C:/.../CSVExperimentLogs/radials_neutral_seed1702")
#   run_overall_accuracy_step_only_battery(1702, pass_threshold = 90.5)
#
# CLI:
#   Rscript overall_accuracy_step_only_battery.R --battery 1702
#   Rscript overall_accuracy_step_only_battery.R --battery 1702 --pass 90.5 --no-plot
#   Rscript overall_accuracy_step_only_battery.R "C:/path/to/run_folder"
# ==============================================================================

suppressPackageStartupMessages({
  library(ggplot2)
})

script_dir <- tryCatch(
  {
    dd <- commandArgs(trailingOnly = FALSE)
    p <- sub("^--file=", "", dd[grepl("^--file=", dd)][1L])
    if (is.na(p) || !nzchar(p)) stop("no --file")
    dirname(normalizePath(p, winslash = "/", mustWork = TRUE))
  },
  error = function(...) normalizePath(getwd(), winslash = "/", mustWork = FALSE)
)

project_root <- Sys.getenv("ATTRACTORS_PROJECT", unset = "")
if (!nzchar(project_root)) {
  project_root <- script_dir
}
project_root <- normalizePath(project_root, winslash = "/", mustWork = FALSE)

src_ext <- file.path(project_root, "overall_accuracy_single_condition_extended.R")
if (!file.exists(src_ext)) {
  stop("overall_accuracy_single_condition_extended.R not found at: ", src_ext)
}
source(src_ext, local = FALSE)

plot_overall_accuracy_step_only <- function(accuracyData, title_run, pass_threshold, fit) {
  d <- accuracyData[order(accuracyData$stage), , drop = FALSE]
  step_df <- NULL
  if (!is.null(fit)) {
    fv <- as.numeric(stats::fitted(fit))
    step_df <- data.frame(stage = d$stage, fit_percent = fv)
  }
  p <- ggplot(d, aes(x = stage, y = accuracyPercent)) +
    geom_line(linewidth = 1.5, color = "steelblue", alpha = 0.8) +
    geom_point(size = 4, color = "steelblue", alpha = 0.9) +
    geom_text(
      aes(label = paste0(passedPatterns, "/", totalPatterns)),
      vjust = -1.2,
      hjust = 0.5,
      size = 3.5,
      color = "gray40"
    ) +
    labs(
      title = "Overall accuracy — step fit only (factor(stage))",
      subtitle = paste0(
        title_run,
        "\nPass ≥ ", pass_threshold, "% recall per pattern",
        "\nGray step = saturated step function (one level per observed stage)"
      ),
      x = "Number of patterns learned (stage)",
      y = "Accuracy (%)"
    ) +
    theme_minimal() +
    theme(
      plot.title = element_text(size = 16, face = "bold", hjust = 0.5),
      plot.subtitle = element_text(size = 10, hjust = 0.5, margin = margin(b = 12)),
      panel.grid.minor = element_blank()
    ) +
    scale_x_continuous(breaks = d$stage, minor_breaks = NULL) +
    scale_y_continuous(limits = c(0, 100), breaks = seq(0, 100, 20)) +
    geom_hline(yintercept = 100, linetype = "dashed", color = "gray70", linewidth = 0.7, alpha = 0.5)

  if (!is.null(step_df) && nrow(step_df) > 0L) {
    p <- p + geom_step(
      data = step_df,
      aes(x = stage, y = fit_percent),
      direction = "hv",
      color = "gray35",
      linewidth = 1,
      inherit.aes = FALSE
    )
  }
  p
}

#' One Unity run: step_saturated fit, plot, CSVs under rgraphs/overall_accuracy_step_only/.
run_overall_accuracy_step_only <- function(
    folder,
    pass_threshold = 80.0,
    save_outputs = TRUE,
    plot_width = 12,
    plot_height = 7,
    show_interactive_plot = TRUE
) {
  folder_in <- path.expand(as.character(folder)[1L])
  if (!nzchar(folder_in)) {
    stop("folder is empty.")
  }
  if (!dir.exists(folder_in)) {
    stop("Experiment folder not found:\n  ", folder_in, call. = FALSE)
  }
  folder <- normalizePath(folder_in, winslash = "/", mustWork = TRUE)
  recallData <- readRecallHistory(folder, quiet = TRUE)
  if (is.null(recallData) || nrow(recallData) == 0L) {
    stop("recall_history.csv missing or empty: ", folder)
  }
  accuracyData <- compute_overall_accuracy_from_recalls(
    recallData,
    pass_threshold,
    verbose = FALSE
  )
  if (nrow(accuracyData) == 0L) {
    stop("No stage rows computed for: ", folder)
  }

  d <- accuracyData[order(accuracyData$stage), , drop = FALSE]
  fit <- oa_fit_step_saturated(d)
  y <- d$accuracyPercent
  if (!is.null(fit)) {
    yhat <- as.numeric(stats::fitted(fit))
  } else {
    yhat <- rep(NA_real_, nrow(d))
  }
  m <- oa_pct_residual_metrics(y, yhat)
  aic <- safe_aic(fit)

  sum_row <- data.frame(
    run_basename = basename(folder),
    pass_threshold = pass_threshold,
    n_stages = nrow(d),
    model = "step_saturated",
    k = if (is.null(fit)) NA_integer_ else length(stats::coef(fit)),
    AIC = aic,
    RMSE_pct = m$RMSE_pct,
    MAE_pct = m$MAE_pct,
    R2_pct = m$R2_pct,
    final_accuracy_pct = tail(d$accuracyPercent, 1L),
    stringsAsFactors = FALSE
  )

  message(
    "step_saturated — ",
    basename(folder),
    "  RMSE_pct=",
    round(m$RMSE_pct, 4),
    "  AIC=",
    round(aic, 2)
  )

  p <- plot_overall_accuracy_step_only(d, basename(folder), pass_threshold, fit)
  if (isTRUE(show_interactive_plot)) {
    print(p)
  }

  out_dir <- file.path(folder, "rgraphs", "overall_accuracy_step_only")
  if (isTRUE(save_outputs)) {
    dir.create(out_dir, recursive = TRUE, showWarnings = TRUE)
    if (!dir.exists(out_dir)) {
      stop("Cannot create: ", out_dir, call. = FALSE)
    }
    ggsave(
      file.path(out_dir, "overall_accuracy_step_only.png"),
      plot = p,
      width = plot_width,
      height = plot_height,
      dpi = 300
    )
    write.csv(sum_row, file.path(out_dir, "overall_accuracy_step_summary.csv"), row.names = FALSE)
    write.csv(d, file.path(out_dir, "overall_accuracy_stage_table.csv"), row.names = FALSE)
    message("Wrote ", out_dir)
  }

  invisible(list(
    accuracy = d,
    fit = fit,
    summary_row = sum_row,
    plot = p,
    out_dir = out_dir
  ))
}

#' All stims × all conditions for one seed (same grid as overall_accuracy_best_fit battery).
run_overall_accuracy_step_only_battery <- function(
    seed,
    log_root = NULL,
    pass_threshold = 80.0,
    stim_sets = OVERALL_ACC_BATTERY_STIMS,
    conds = OVERALL_ACC_BATTERY_CONDS,
    save_outputs = TRUE,
    show_interactive_plot = TRUE,
    aggregate_parent = NULL
) {
  if (is.null(log_root)) {
    log_root <- default_log_root_overall_acc()
  } else {
    log_root <- normalizePath(log_root, winslash = "/", mustWork = FALSE)
  }
  if (!dir.exists(log_root)) {
    stop("log_root not found: ", log_root)
  }

  rows <- list()
  for (stim in stim_sets) {
    for (cond in conds) {
      f <- resolve_battery_run_folder_overall(log_root, stim, cond, seed)
      if (is.na(f) || !nzchar(f)) {
        warning(
          "Skipping ", stim, "_", cond, "_seed", seed, " — no recall_history.csv",
          call. = FALSE,
          immediate. = TRUE
        )
        next
      }
      message("\n=== ", basename(f), " ===")
      res <- run_overall_accuracy_step_only(
        f,
        pass_threshold = pass_threshold,
        save_outputs = save_outputs,
        show_interactive_plot = show_interactive_plot
      )
      rows[[basename(f)]] <- res$summary_row
    }
  }

  combined <- if (length(rows) > 0L) {
    do.call(rbind, rows)
  } else {
    NULL
  }

  if (!is.null(combined) && isTRUE(save_outputs)) {
    agg_fig_parent <- oa_aggregate_figures_parent(log_root, aggregate_parent)
    agg_dir <- file.path(agg_fig_parent, paste0("overall_accuracy_step_only_seed_", seed))
    dir.create(agg_dir, recursive = TRUE, showWarnings = TRUE)
    if (!dir.exists(agg_dir)) {
      stop("Cannot create aggregate directory:\n  ", agg_dir, call. = FALSE)
    }
    agg_csv <- file.path(agg_dir, "combined_step_only_summary.csv")
    write.csv(combined, agg_csv, row.names = FALSE)
    message("\nCombined step-only summary:\n  ", agg_csv)
  }

  invisible(list(combined = combined, by_run = rows))
}

if (!interactive()) {
  args <- commandArgs(trailingOnly = TRUE)
  idxb <- match("--battery", args)
  if (!is.na(idxb) && idxb < length(args)) {
    seed <- args[idxb + 1L]
    pt <- 80.0
    idxp <- match("--pass", args)
    if (!is.na(idxp) && idxp < length(args)) {
      pt <- as.numeric(args[idxp + 1L])
    }
    agg_par <- NULL
    idxa <- match("--aggregate-parent", args)
    if (!is.na(idxa) && idxa < length(args)) {
      agg_par <- args[idxa + 1L]
    }
    lr <- NULL
    idxl <- match("--log-root", args)
    if (!is.na(idxl) && idxl < length(args)) {
      lr <- args[idxl + 1L]
    }
    no_plot <- !is.na(match("--no-plot", args))
    run_overall_accuracy_step_only_battery(
      seed,
      log_root = lr,
      pass_threshold = pt,
      show_interactive_plot = !no_plot,
      aggregate_parent = agg_par
    )
    quit(save = "no", status = 0L)
  }
  nonopt <- args[!grepl("^--", args)]
  if (length(nonopt) >= 1L) {
    run_folder <- nonopt[1L]
    pt <- 80.0
    idxp <- match("--pass", args)
    if (!is.na(idxp) && idxp < length(args)) {
      pt <- as.numeric(args[idxp + 1L])
    }
    no_plot <- !is.na(match("--no-plot", args))
    run_overall_accuracy_step_only(
      run_folder,
      pass_threshold = pt,
      show_interactive_plot = !no_plot
    )
    quit(save = "no", status = 0L)
  }
}
