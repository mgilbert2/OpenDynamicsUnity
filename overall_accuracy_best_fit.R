# ==============================================================================
# Overall accuracy + model fits / plotted curve (no pyramids / no combinedrecalloverlays)
# ==============================================================================
# Uses the same stage-wise accuracy definition as VisualizeOverallAccuracy.R.
# Writes under each run folder:
#   rgraphs/overall_accuracy_best_fit/overall_accuracy_best_fit.png
#   rgraphs/overall_accuracy_best_fit/overall_accuracy_stage_table.csv
#   rgraphs/overall_accuracy_best_fit/overall_accuracy_model_comparison.csv  (AIC all models)
#   rgraphs/overall_accuracy_best_fit/overall_accuracy_best_fit_summary.csv  (one wide row per run)
#
# Battery also writes:
#   .../combined_all_models_metrics_long_seed_<seed>.csv  — all runs × all models (RMSE, MAE, R2, AIC, flags)
#   .../battery_bundle_<timestamp>/  — new folder each battery invocation:
#       figures/   — copy of each run's overall_accuracy_best_fit.png (named by run)
#       tables/    — copies of combined CSVs + by_run_summaries/*.csv
#       ANALYSIS_SUMMARY.txt  — plain-English overview + key tables
#   Set write_battery_bundle = FALSE to skip the bundle. CLI: --no-bundle
#
# Models (same spirit as forgetting_curve_fits.R): linear, exponential_asymp (nls SSasympOrig),
#   power_loglog (lm on log accuracy vs log stage — AIC on log scale, exploratory vs others),
#   inverse_stage (accuracy ~ 1/stage).
#
# Battery (2×4, same folder names as experiment_master):
#   Default: <log_root>/_postprocess_figures/overall_accuracy_best_fit_seed_<seed>/combined_*.csv
#   If that path is not writable (common under LocalLow), set either:
#     aggregate_parent = "C:/Users/Mak/Attractors/overall_accuracy_output"
#   or env ATTRACTORS_OVERALL_ACC_AGG_DIR to a writable folder; files go under
#     <aggregate_parent>/overall_accuracy_best_fit_seed_<seed>/
#
# Usage (R):
#   setwd("C:/Users/Mak/Attractors")
#   source("overall_accuracy_best_fit.R")
#   # folder = full path to ONE Unity run directory that contains recall_history.csv (not a placeholder):
#   run_overall_accuracy_best_fit(
#     "C:/Users/<You>/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/radials_neutral_seed1702",
#     pass_threshold = 90.5,
#     curve_model = "power_loglog"
#   )
#   run_overall_accuracy_best_fit_battery(1702, pass_threshold = 90.5, curve_model = "power_loglog")
#   run_overall_accuracy_best_fit_battery(1702, aggregate_parent = "C:/Users/Mak/Attractors/overall_accuracy_output")
#
# Fair comparison: each model gets RMSE / MAE / R² on the original % scale at observed stages.
#   best_criterion = "rmse_pct" (default) — which model draws when curve_model = "aic_best"
#   best_criterion = "aic_comparable" — min AIC among linear, exponential_asymp, inverse_stage only
#   best_criterion = "aic_all" — min AIC among all four (power_loglog AIC is on log-y scale)
# Battery also writes combined_all_models_metrics_long_seed_<seed>.csv (every run × every model).
#
# PowerShell:
#   Rscript overall_accuracy_best_fit.R "C:/path/to/run_folder"
#   Rscript overall_accuracy_best_fit.R --battery 1702
#   Rscript overall_accuracy_best_fit.R --battery 1702 --pass 90.5 --curve power_loglog
#   Rscript overall_accuracy_best_fit.R --battery 1702 --best rmse_pct
#   Rscript overall_accuracy_best_fit.R --battery 1702 --aggregate-parent "C:/Users/Mak/Attractors/overall_accuracy_output"
# ==============================================================================

suppressPackageStartupMessages({
  library(ggplot2)
})

script_dir <- tryCatch(
  {
    dd <- commandArgs(trailingOnly = FALSE)
    p <- sub("^--file=", "", dd[grepl("^--file=", dd)][1L])
    if (is.na(p) || !nzchar(p)) {
      stop("no --file")
    }
    dirname(normalizePath(p, winslash = "/", mustWork = TRUE))
  },
  error = function(...) {
    normalizePath(getwd(), winslash = "/", mustWork = FALSE)
  }
)

project_root <- Sys.getenv("ATTRACTORS_PROJECT", unset = "")
if (!nzchar(project_root)) {
  project_root <- script_dir
}
project_root <- normalizePath(project_root, winslash = "/", mustWork = FALSE)
src_voa <- file.path(project_root, "VisualizeOverallAccuracy.R")
if (!file.exists(src_voa)) {
  stop("VisualizeOverallAccuracy.R not found at: ", src_voa, "\nSet ATTRACTORS_PROJECT or setwd() to project root.")
}
source(src_voa, local = FALSE)

OVERALL_ACC_BATTERY_STIMS <- c("radials", "geometric20")
OVERALL_ACC_BATTERY_CONDS <- c("neutral", "condA", "condB", "condC")

default_log_root_overall_acc <- function() {
  normalizePath(
    Sys.getenv(
      "ATTRACTORS_LOGS",
      unset = file.path(
        Sys.getenv("USERPROFILE"),
        "AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs"
      )
    ),
    winslash = "/",
    mustWork = FALSE
  )
}

#' Writable parent for combined CSVs + battery_bundle_* (not Unity run folders).
#' Default: <log_root>/_postprocess_figures. Override if Windows blocks writes there.
oa_aggregate_figures_parent <- function(log_root, aggregate_parent = NULL) {
  ap <- aggregate_parent
  if (is.null(ap) || !nzchar(trimws(as.character(ap)[1L]))) {
    env <- Sys.getenv("ATTRACTORS_OVERALL_ACC_AGG_DIR", unset = "")
    if (nzchar(env)) {
      ap <- env
    }
  }
  if (!is.null(ap) && nzchar(trimws(as.character(ap)[1L]))) {
    normalizePath(path.expand(trimws(as.character(ap)[1L])), winslash = "/", mustWork = FALSE)
  } else {
    file.path(log_root, "_postprocess_figures")
  }
}

resolve_battery_run_folder_overall <- function(log_root, stim, cond, seed) {
  seed <- as.character(seed)[1L]
  bn1 <- paste0(stim, "_", cond, "_seed", seed)
  bn2 <- paste0(bn1, "_seed", seed)
  p1 <- file.path(log_root, bn1)
  p2 <- file.path(log_root, bn2)
  if (file.exists(file.path(p1, "recall_history.csv"))) {
    return(p1)
  }
  if (file.exists(file.path(p2, "recall_history.csv"))) {
    return(p2)
  }
  NA_character_
}

safe_aic <- function(fit) {
  if (is.null(fit)) {
    return(NA_real_)
  }
  tryCatch(as.numeric(stats::AIC(fit)), error = function(...) NA_real_)
}

oa_fit_linear <- function(d) {
  if (nrow(d) < 2L) {
    return(NULL)
  }
  tryCatch(stats::lm(accuracyPercent ~ stage, data = d), error = function(...) NULL)
}

oa_fit_power_loglog <- function(d) {
  eps <- 1e-2
  d <- d[order(d$stage), , drop = FALSE]
  d <- d[d$stage > 0L, , drop = FALSE]
  if (nrow(d) < 3L) {
    return(NULL)
  }
  d$accClip <- pmax(d$accuracyPercent, eps)
  tryCatch(stats::lm(log(accClip) ~ log(stage), data = d), error = function(...) NULL)
}

oa_fit_inverse_stage <- function(d) {
  if (nrow(d) < 3L) {
    return(NULL)
  }
  z <- d$stage
  if (any(z <= 0L)) {
    return(NULL)
  }
  tryCatch(stats::lm(accuracyPercent ~ I(1 / stage), data = d), error = function(...) NULL)
}

oa_fit_exponential_asymp <- function(d) {
  if (nrow(d) < 4L) {
    return(NULL)
  }
  d <- d[order(d$stage), , drop = FALSE]
  t0 <- min(d$stage)
  t <- d$stage - t0
  y <- d$accuracyPercent
  fit <- tryCatch(stats::nls(y ~ stats::SSasympOrig(t, Asym, lrc, c0)), error = function(...) NULL)
  if (!is.null(fit)) {
    attr(fit, "t0") <- t0
  }
  fit
}

oa_predict_exp <- function(fit, stages) {
  t0 <- attr(fit, "t0")
  if (is.null(t0)) {
    t0 <- 0
  }
  stats::predict(fit, newdata = data.frame(t = stages - t0))
}

oa_predict_best <- function(best, fits, stages) {
  if (is.na(best) || length(stages) == 0L) {
    return(rep(NA_real_, length(stages)))
  }
  out <- tryCatch(
    {
      if (best == "linear" && !is.null(fits$linear)) {
        return(stats::predict(fits$linear, newdata = data.frame(stage = stages)))
      }
      if (best == "exponential_asymp" && !is.null(fits$exponential_asymp)) {
        return(oa_predict_exp(fits$exponential_asymp, stages))
      }
      if (best == "power_loglog" && !is.null(fits$power_loglog)) {
        nd <- data.frame(stage = stages)
        return(exp(stats::predict(fits$power_loglog, newdata = nd)))
      }
      if (best == "inverse_stage" && !is.null(fits$inverse_stage)) {
        return(stats::predict(fits$inverse_stage, newdata = data.frame(stage = stages)))
      }
      rep(NA_real_, length(stages))
    },
    error = function(...) rep(NA_real_, length(stages))
  )
  ifelse(is.finite(out), pmin(100, pmax(0, out)), NA_real_)
}

oa_model_row <- function(label, fit, n_obs) {
  k <- if (is.null(fit)) {
    NA_integer_
  } else {
    length(stats::coef(fit))
  }
  data.frame(
    model = label,
    n = n_obs,
    k = k,
    AIC = safe_aic(fit),
    stringsAsFactors = FALSE
  )
}

oa_pick_best_model <- function(rows) {
  r <- rows[is.finite(rows$AIC), , drop = FALSE]
  if (nrow(r) == 0L) {
    return(NA_character_)
  }
  r$model[which.min(r$AIC)]
}

#' Residual metrics on the original accuracy % scale (same y for every model).
oa_pct_residual_metrics <- function(y, yhat) {
  ok <- is.finite(y) & is.finite(yhat)
  if (!any(ok)) {
    return(list(RMSE_pct = NA_real_, MAE_pct = NA_real_, R2_pct = NA_real_, n_fitted = 0L))
  }
  y <- y[ok]
  yhat <- yhat[ok]
  e <- y - yhat
  rmse <- sqrt(mean(e^2))
  mae <- mean(abs(e))
  sst <- sum((y - mean(y))^2)
  r2 <- if (sst > 1e-12) {
    1 - sum(e^2) / sst
  } else {
    NA_real_
  }
  list(RMSE_pct = rmse, MAE_pct = mae, R2_pct = r2, n_fitted = length(y))
}

OA_AIC_COMPARABLE_MODELS <- c("linear", "exponential_asymp", "inverse_stage")

oa_pick_best_rmse_pct <- function(comp) {
  sub <- comp[is.finite(comp$RMSE_pct), , drop = FALSE]
  if (nrow(sub) == 0L) {
    return(NA_character_)
  }
  sub$model[which.min(sub$RMSE_pct)]
}

oa_pick_best_aic_comparable <- function(comp) {
  sub <- comp[comp$model %in% OA_AIC_COMPARABLE_MODELS & is.finite(comp$AIC), , drop = FALSE]
  if (nrow(sub) == 0L) {
    return(NA_character_)
  }
  sub$model[which.min(sub$AIC)]
}

oa_primary_best_name <- function(cmp, best_criterion = "rmse_pct") {
  best_criterion <- match.arg(
    best_criterion,
    c("rmse_pct", "aic_all", "aic_comparable")
  )
  nm <- switch(
    best_criterion,
    rmse_pct = cmp$best_name_rmse_pct,
    aic_all = cmp$best_name_aic_all,
    aic_comparable = cmp$best_name_aic_comparable
  )
  if (length(nm) != 1L || is.na(nm) || !nzchar(nm)) {
    nm <- cmp$best_name_rmse_pct
  }
  if (length(nm) != 1L || is.na(nm) || !nzchar(nm)) {
    nm <- cmp$best_name_aic_comparable
  }
  if (length(nm) != 1L || is.na(nm) || !nzchar(nm)) {
    nm <- cmp$best_name_aic_all
  }
  nm
}

OA_CURVE_MODELS <- c("aic_best", "linear", "exponential_asymp", "power_loglog", "inverse_stage")

#' Which model to draw as the red curve (primary-best vs a chosen model if that fit exists).
oa_resolve_plotted_model <- function(curve_model, cmp) {
  curve_model <- curve_model[1L]
  if (!curve_model %in% OA_CURVE_MODELS) {
    stop("curve_model must be one of: ", paste(OA_CURVE_MODELS, collapse = ", "))
  }
  if (identical(as.character(curve_model), "aic_best")) {
    return(list(name = cmp$best_name, fallback_from = NA_character_))
  }
  fit <- cmp$fits[[curve_model]]
  if (!is.null(fit)) {
    return(list(name = curve_model, fallback_from = NA_character_))
  }
  warning(
    "curve_model='", curve_model, "' did not converge; plotting primary-best curve instead.",
    call. = FALSE,
    immediate. = TRUE
  )
  list(name = cmp$best_name, fallback_from = as.character(curve_model))
}

#' Fit competing models to stage × overall accuracy %; add %-scale metrics and explicit "best" flags.
oa_compare_models <- function(accuracyData) {
  d <- accuracyData[order(accuracyData$stage), , drop = FALSE]
  n_obs <- nrow(d)
  fits <- list(
    linear = oa_fit_linear(d),
    exponential_asymp = oa_fit_exponential_asymp(d),
    power_loglog = oa_fit_power_loglog(d),
    inverse_stage = oa_fit_inverse_stage(d)
  )
  comp <- rbind(
    oa_model_row("linear", fits$linear, n_obs),
    oa_model_row("exponential_asymp", fits$exponential_asymp, n_obs),
    oa_model_row("power_loglog", fits$power_loglog, n_obs),
    oa_model_row("inverse_stage", fits$inverse_stage, n_obs)
  )
  best_aic_all <- oa_pick_best_model(comp)
  comp$best_by_aic_all <- comp$model == best_aic_all

  stages <- d$stage
  y <- d$accuracyPercent
  rmse_v <- mae_v <- r2_v <- rep(NA_real_, nrow(comp))
  for (i in seq_len(nrow(comp))) {
    mn <- comp$model[i]
    yhat <- oa_predict_best(mn, fits, stages)
    m <- oa_pct_residual_metrics(y, yhat)
    rmse_v[i] <- m$RMSE_pct
    mae_v[i] <- m$MAE_pct
    r2_v[i] <- m$R2_pct
  }
  comp$RMSE_pct <- rmse_v
  comp$MAE_pct <- mae_v
  comp$R2_pct <- r2_v

  best_rmse <- oa_pick_best_rmse_pct(comp)
  comp$best_by_rmse_pct <- comp$model == best_rmse

  best_aic_comp <- oa_pick_best_aic_comparable(comp)
  comp$best_by_aic_comparable <- comp$model == best_aic_comp

  list(
    fits = fits,
    comparison = comp,
    data = d,
    best_name_aic_all = best_aic_all,
    best_name_rmse_pct = best_rmse,
    best_name_aic_comparable = best_aic_comp
  )
}

oa_best_fit_curve_df <- function(cmp, plotted_model_name, n_grid = 80L) {
  d <- cmp$data
  xs <- seq(min(d$stage), max(d$stage), length.out = n_grid)
  yhat <- oa_predict_best(plotted_model_name, cmp$fits, xs)
  data.frame(stage = xs, fit_percent = yhat)
}

oa_metric_for_model <- function(comp, model, col) {
  if (length(model) != 1L || is.na(model) || !nzchar(as.character(model))) {
    return(NA_real_)
  }
  hit <- comp$model == model
  if (!any(hit)) {
    return(NA_real_)
  }
  v <- comp[[col]][hit]
  v[[1L]]
}

oa_best_fit_summary_row <- function(folder, pass_threshold, accuracyData, cmp, plotted_name, best_criterion) {
  bn <- basename(as.character(folder))
  n <- nrow(accuracyData)
  comp <- cmp$comparison
  prim <- cmp$best_name
  aic_plot <- oa_metric_for_model(comp, plotted_name, "AIC")
  rmse_plot <- oa_metric_for_model(comp, plotted_name, "RMSE_pct")
  data.frame(
    run_basename = bn,
    pass_threshold = pass_threshold,
    n_stages = n,
    best_criterion = best_criterion,
    primary_best_model = prim,
    primary_AIC = oa_metric_for_model(comp, prim, "AIC"),
    primary_RMSE_pct = oa_metric_for_model(comp, prim, "RMSE_pct"),
    best_model = prim,
    best_AIC = oa_metric_for_model(comp, prim, "AIC"),
    best_by_rmse_pct_model = cmp$best_name_rmse_pct,
    best_by_rmse_pct_value = oa_metric_for_model(comp, cmp$best_name_rmse_pct, "RMSE_pct"),
    best_by_aic_comparable_model = cmp$best_name_aic_comparable,
    best_by_aic_comparable_AIC = oa_metric_for_model(comp, cmp$best_name_aic_comparable, "AIC"),
    best_by_aic_all_model = cmp$best_name_aic_all,
    best_by_aic_all_AIC = oa_metric_for_model(comp, cmp$best_name_aic_all, "AIC"),
    plotted_curve_model = plotted_name,
    plotted_curve_AIC = aic_plot,
    plotted_curve_RMSE_pct = rmse_plot,
    final_accuracy_pct = if (n) tail(accuracyData$accuracyPercent, 1L) else NA_real_,
    stringsAsFactors = FALSE
  )
}

plot_overall_accuracy_best_fit <- function(
    accuracyData,
    title_run,
    pass_threshold,
    cmp,
    curve_model = "aic_best",
    best_criterion = "rmse_pct"
) {
  pm <- oa_resolve_plotted_model(curve_model, cmp)
  plotted_name <- pm$name
  curve_df <- oa_best_fit_curve_df(cmp, plotted_name)
  curve_df <- curve_df[is.finite(curve_df$fit_percent), , drop = FALSE]
  best_lab <- cmp$best_name
  if (length(best_lab) != 1L || is.na(best_lab)) {
    best_lab <- "(none converged)"
  }
  red_legend <- if (identical(as.character(curve_model), "aic_best")) {
    paste0(
      "Red = primary pick by ", best_criterion, ": ",
      ifelse(is.na(plotted_name), "?", plotted_name),
      " (see CSV: RMSE_pct / MAE_pct / R2_pct on % scale; AIC flags)"
    )
  } else {
    paste0(
      "Red = ", ifelse(is.na(plotted_name), "?", plotted_name),
      " curve",
      if (length(pm$fallback_from) == 1L && !is.na(pm$fallback_from) && nzchar(pm$fallback_from)) {
        paste0(" (requested ", pm$fallback_from, " failed; fell back to ", best_lab, ")")
      } else if (!identical(plotted_name, best_lab)) {
        paste0(" (primary-best was ", best_lab, " — see CSV)")
      } else {
        ""
      }
    )
  }

  p <- ggplot(accuracyData, aes(x = stage, y = accuracyPercent)) +
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
      title = "Overall accuracy + fitted curve",
      subtitle = paste0(
        title_run,
        "\nPass ≥ ", pass_threshold, "% recall per pattern",
        "\n", red_legend,
        "\n(power_loglog: log-linear on accuracy; its AIC is not comparable to the others)"
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
    scale_x_continuous(breaks = accuracyData$stage, minor_breaks = NULL) +
    scale_y_continuous(limits = c(0, 100), breaks = seq(0, 100, 20)) +
    geom_hline(yintercept = 100, linetype = "dashed", color = "gray70", linewidth = 0.7, alpha = 0.5)

  if (nrow(curve_df) > 0L) {
    p <- p + geom_line(
      data = curve_df,
      aes(x = stage, y = fit_percent),
      color = "firebrick",
      linewidth = 1,
      inherit.aes = FALSE
    )
  }
  p
}

#' One experiment folder: stage table + model comparison + best-fit summary + PNG (no pyramids).
run_overall_accuracy_best_fit <- function(
    folder,
    pass_threshold = 80.0,
    save_outputs = TRUE,
    plot_width = 12,
    plot_height = 7,
    curve_model = "aic_best",
    best_criterion = "rmse_pct",
    show_interactive_plot = TRUE
) {
  folder_in <- path.expand(as.character(folder)[1L])
  if (!nzchar(folder_in)) {
    stop("folder is empty — pass the full path to the Unity run directory (contains recall_history.csv).")
  }
  if (!dir.exists(folder_in)) {
    stop(
      "Experiment folder not found:\n  ", folder_in,
      "\nUse the real path to your run (e.g. .../CSVExperimentLogs/radials_neutral_seed1702). ",
      "Do not use documentation placeholders like C:/.../your_run_folder.",
      call. = FALSE
    )
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

  cmp <- oa_compare_models(accuracyData)
  best_criterion <- match.arg(best_criterion, c("rmse_pct", "aic_all", "aic_comparable"))
  cmp$best_criterion <- best_criterion
  cmp$best_name <- oa_primary_best_name(cmp, best_criterion)
  pm <- oa_resolve_plotted_model(curve_model, cmp)
  row_best <- oa_best_fit_summary_row(
    folder,
    pass_threshold,
    accuracyData,
    cmp,
    plotted_name = pm$name,
    best_criterion = best_criterion
  )
  message(
    "Primary best (", best_criterion, "): ",
    cmp$best_name,
    " — ",
    row_best$run_basename
  )
  message("Per-model metrics (RMSE/MAE/R2 on % scale at observed stages; AIC as reported by R):")
  print(
    cmp$comparison[
      ,
      c(
        "model",
        "AIC",
        "RMSE_pct",
        "MAE_pct",
        "R2_pct",
        "best_by_rmse_pct",
        "best_by_aic_comparable",
        "best_by_aic_all"
      ),
      drop = FALSE
    ]
  )

  p <- plot_overall_accuracy_best_fit(
    accuracyData,
    title_run = basename(folder),
    pass_threshold,
    cmp,
    curve_model = curve_model,
    best_criterion = best_criterion
  )
  if (isTRUE(show_interactive_plot)) {
    print(p)
  }

  out_dir <- file.path(folder, "rgraphs", "overall_accuracy_best_fit")
  if (isTRUE(save_outputs)) {
    dir.create(out_dir, recursive = TRUE, showWarnings = TRUE)
    if (!dir.exists(out_dir)) {
      stop(
        "Cannot create output directory (check Unity log folder is writable):\n  ",
        out_dir,
        call. = FALSE
      )
    }
    png_path <- file.path(out_dir, "overall_accuracy_best_fit.png")
    tryCatch(
      {
        ggsave(png_path, plot = p, width = plot_width, height = plot_height, dpi = 300)
        message("Wrote ", png_path)
        write.csv(
          accuracyData,
          file.path(out_dir, "overall_accuracy_stage_table.csv"),
          row.names = FALSE
        )
        write.csv(
          cmp$comparison,
          file.path(out_dir, "overall_accuracy_model_comparison.csv"),
          row.names = FALSE
        )
        write.csv(
          row_best,
          file.path(out_dir, "overall_accuracy_best_fit_summary.csv"),
          row.names = FALSE
        )
        message("Wrote stage table, model comparison, best-fit summary under ", out_dir)
      },
      error = function(e) {
        stop(
          "Failed writing under experiment folder (read-only path, antivirus, or bad path?):\n  ",
          out_dir,
          "\n",
          conditionMessage(e),
          call. = FALSE
        )
      }
    )
  }

  invisible(list(
    accuracy = accuracyData,
    model_comparison = cmp$comparison,
    best_fit_row = row_best,
    fits = cmp$fits,
    best_model = cmp$best_name,
    best_criterion = best_criterion,
    plot = p,
    out_dir = out_dir
  ))
}

oa_df_text <- function(df, width = 200L) {
  if (is.null(df) || nrow(df) == 0L) {
    return("(no rows)")
  }
  df <- as.data.frame(df, stringsAsFactors = FALSE)
  tryCatch(
    {
      cn <- colnames(df)
      mat <- format(df, trim = TRUE, justify = "left")
      hdr <- paste(cn, collapse = "  |  ")
      rows <- apply(mat, 1L, function(r) paste(as.character(r), collapse = "  |  "))
      paste(c(hdr, rows), collapse = "\n")
    },
    error = function(e) {
      paste0("(table formatting skipped: ", conditionMessage(e), ")")
    }
  )
}

oa_rmse_winners_from_long <- function(combined_long) {
  if (is.null(combined_long) || nrow(combined_long) == 0L) {
    return(NULL)
  }
  rows <- list()
  for (rb in unique(combined_long$run_basename)) {
    sub <- combined_long[
      combined_long$run_basename == rb &
        !is.na(combined_long$best_by_rmse_pct) &
        combined_long$best_by_rmse_pct,
      ,
      drop = FALSE
    ]
    if (nrow(sub) == 0L) {
      next
    }
    rows[[rb]] <- sub[1L, c("run_basename", "model", "RMSE_pct", "MAE_pct", "R2_pct", "AIC"), drop = FALSE]
  }
  if (length(rows) == 0L) {
    return(NULL)
  }
  do.call(rbind, rows)
}

#' Write ANALYSIS_SUMMARY.txt into bundle_dir.
oa_write_battery_analysis_summary <- function(
    bundle_dir,
    seed,
    pass_threshold,
    best_criterion,
    curve_model,
    log_root,
    combined,
    combined_long
) {
  bundle_line <- tryCatch(
    normalizePath(bundle_dir, winslash = "/", mustWork = FALSE),
    error = function(...) bundle_dir
  )
  if (length(bundle_line) != 1L || is.na(bundle_line)) {
    bundle_line <- bundle_dir
  }

  lines <- c(
    "OVERALL ACCURACY - FULL BATTERY ANALYSIS",
    strrep("=", 72L),
    "",
    paste0("Generated (local time): ", format(Sys.time(), "%Y-%m-%d %H:%M:%S")),
    paste0("Log root: ", log_root),
    paste0("Seed: ", seed),
    paste0("Pass threshold (% recall per pattern): ", pass_threshold),
    paste0(
      "best_criterion: ", best_criterion,
      "  (which model is \"primary\" when curve_model = \"aic_best\")"
    ),
    paste0("curve_model: ", curve_model),
    "",
    "WHAT WAS COMPUTED",
    strrep("-", 72L),
    "For each Unity run: stage-wise overall accuracy (% patterns passing recall threshold),",
    "then four fits (linear, exponential_asymp, power_loglog, inverse_stage).",
    "RMSE_pct / MAE_pct / R2_pct are on the original % scale at observed stages.",
    "AIC is as reported by R (power_loglog is on log-y scale - not comparable to others).",
    "Columns best_by_rmse_pct, best_by_aic_comparable, best_by_aic_all mark winners.",
    "",
    "THIS BUNDLE",
    strrep("-", 72L),
    bundle_line,
    "  figures/  - PNG per run (overall accuracy + fitted curve)",
    "  tables/   - combined CSVs + one summary CSV per run",
    ""
  )

  if (!is.null(combined) && nrow(combined) > 0L) {
    cols <- intersect(
      c(
        "run_basename",
        "primary_best_model",
        "primary_RMSE_pct",
        "best_by_rmse_pct_model",
        "best_by_rmse_pct_value",
        "best_by_aic_comparable_model",
        "best_by_aic_all_model",
        "final_accuracy_pct"
      ),
      names(combined)
    )
    lines <- c(
      lines,
      "PER-RUN OVERVIEW (from combined_best_fit_summary.csv)",
      strrep("-", 72L),
      oa_df_text(combined[, cols, drop = FALSE]),
      "",
      "PRIMARY BEST MODEL - HOW MANY RUNS",
      strrep("-", 72L)
    )
    tab <- table(combined$primary_best_model, useNA = "ifany")
    lines <- c(lines, oa_df_text(as.data.frame(tab, stringsAsFactors = FALSE)))
  }

  rw <- oa_rmse_winners_from_long(combined_long)
  if (!is.null(rw) && nrow(rw) > 0L) {
    lines <- c(
      lines,
      "",
      "LOWEST RMSE (ON % SCALE) - WINNING MODEL PER RUN",
      strrep("-", 72L),
      oa_df_text(rw)
    )
  }

  lines <- c(
    lines,
    "",
    "SPREADSHEETS (open in Excel / R)",
    strrep("-", 72L),
    "  tables/combined_best_fit_summary.csv",
    "  tables/combined_all_models_metrics_long_seed_<seed>.csv",
    ""
  )

  outf <- file.path(bundle_dir, "ANALYSIS_SUMMARY.txt")
  if (!dir.exists(bundle_dir)) {
    stop("Bundle directory does not exist (cannot write summary): ", bundle_dir, call. = FALSE)
  }
  lines <- as.character(lines)
  lines[is.na(lines)] <- ""
  tryCatch(
    writeLines(lines, outf, useBytes = FALSE),
    error = function(e) {
      stop(
        "Cannot write ANALYSIS_SUMMARY.txt (check folder permissions and path length):\n  ",
        outf,
        "\nOriginal error: ",
        conditionMessage(e),
        call. = FALSE
      )
    }
  )
  message("Wrote readable summary: ", outf)
  invisible(outf)
}

#' Copy figures and tables into a new timestamped folder under the seed postprocess dir.
oa_write_battery_bundle <- function(
    agg_dir,
    seed,
    pass_threshold,
    best_criterion,
    curve_model,
    log_root,
    combined,
    combined_long,
    agg_csv,
    long_csv,
    run_paths
) {
  if (!dir.exists(agg_dir)) {
    stop("aggregate directory missing (cannot write battery bundle): ", agg_dir, call. = FALSE)
  }
  bundle_id <- format(Sys.time(), "%Y%m%d_%H%M%S")
  bundle_root <- file.path(agg_dir, paste0("battery_bundle_", bundle_id))
  dir.create(bundle_root, recursive = TRUE, showWarnings = TRUE)
  if (!dir.exists(bundle_root)) {
    stop(
      "Failed to create battery bundle folder (permissions or invalid path?):\n  ",
      bundle_root,
      call. = FALSE
    )
  }
  fig_dir <- file.path(bundle_root, "figures")
  tab_dir <- file.path(bundle_root, "tables")
  by_run_dir <- file.path(tab_dir, "by_run_summaries")
  dir.create(fig_dir, recursive = TRUE, showWarnings = TRUE)
  dir.create(by_run_dir, recursive = TRUE, showWarnings = TRUE)
  if (!dir.exists(fig_dir) || !dir.exists(tab_dir) || !dir.exists(by_run_dir)) {
    stop(
      "Failed to create figures/ or tables/ under bundle:\n  ",
      bundle_root,
      call. = FALSE
    )
  }

  if (file.exists(agg_csv)) {
    file.copy(agg_csv, file.path(tab_dir, basename(agg_csv)), overwrite = TRUE)
  }
  if (!is.null(long_csv) && nzchar(long_csv) && file.exists(long_csv)) {
    file.copy(long_csv, file.path(tab_dir, basename(long_csv)), overwrite = TRUE)
  }

  for (tag in names(run_paths)) {
    rd <- run_paths[[tag]]
    png_src <- file.path(rd, "rgraphs", "overall_accuracy_best_fit", "overall_accuracy_best_fit.png")
    safe_tag <- gsub("[^A-Za-z0-9._-]+", "_", tag)
    if (file.exists(png_src)) {
      file.copy(
        png_src,
        file.path(fig_dir, paste0(safe_tag, "_overall_accuracy_best_fit.png")),
        overwrite = TRUE
      )
    }
    sum_src <- file.path(rd, "rgraphs", "overall_accuracy_best_fit", "overall_accuracy_best_fit_summary.csv")
    if (file.exists(sum_src)) {
      file.copy(sum_src, file.path(by_run_dir, paste0(safe_tag, "_summary.csv")), overwrite = TRUE)
    }
  }

  oa_write_battery_analysis_summary(
    bundle_root,
    seed = seed,
    pass_threshold = pass_threshold,
    best_criterion = best_criterion,
    curve_model = curve_model,
    log_root = log_root,
    combined = combined,
    combined_long = combined_long
  )

  message("\nBattery bundle (figures + tables + ANALYSIS_SUMMARY.txt):\n  ", bundle_root)
  invisible(bundle_root)
}

#' All 2×4 cells for one seed (same layout as experiment_master).
run_overall_accuracy_best_fit_battery <- function(
    seed,
    log_root = NULL,
    pass_threshold = 80.0,
    stim_sets = OVERALL_ACC_BATTERY_STIMS,
    conds = OVERALL_ACC_BATTERY_CONDS,
    save_outputs = TRUE,
    curve_model = "aic_best",
    best_criterion = "rmse_pct",
    write_battery_bundle = TRUE,
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

  fit_rows <- list()
  metrics_long <- list()
  run_paths <- list()
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
      res <- run_overall_accuracy_best_fit(
        f,
        pass_threshold = pass_threshold,
        save_outputs = save_outputs,
        curve_model = curve_model,
        best_criterion = best_criterion,
        show_interactive_plot = FALSE
      )
      tag <- basename(f)
      fit_rows[[tag]] <- res$best_fit_row
      run_paths[[tag]] <- f
      metrics_long[[tag]] <- data.frame(
        run_basename = tag,
        seed = as.character(seed)[1L],
        pass_threshold = pass_threshold,
        best_criterion = best_criterion,
        res$model_comparison,
        stringsAsFactors = FALSE
      )
    }
  }

  combined <- if (length(fit_rows) > 0L) {
    do.call(rbind, fit_rows)
  } else {
    NULL
  }
  combined_long <- if (length(metrics_long) > 0L) {
    do.call(rbind, metrics_long)
  } else {
    NULL
  }
  bundle_root <- NULL
  aggregate_output_dir <- NULL
  if (!is.null(combined) && isTRUE(save_outputs)) {
    agg_fig_parent <- oa_aggregate_figures_parent(log_root, aggregate_parent)
    agg_dir <- file.path(agg_fig_parent, paste0("overall_accuracy_best_fit_seed_", seed))
    message(
      "Combined CSVs / battery bundle directory:\n  ",
      agg_dir,
      "\n(parent: ",
      agg_fig_parent,
      ")"
    )
    dir.create(agg_dir, recursive = TRUE, showWarnings = TRUE)
    if (!dir.exists(agg_dir)) {
      stop("Cannot create aggregate directory:\n  ", agg_dir, call. = FALSE)
    }
    agg_csv <- file.path(agg_dir, "combined_best_fit_summary.csv")
    tryCatch(
      write.csv(combined, agg_csv, row.names = FALSE),
      error = function(e) {
        stop(
          "Cannot write combined summary CSV:\n  ",
          agg_csv,
          "\n",
          conditionMessage(e),
          "\n\nFix: pass a writable folder, e.g.\n",
          "  run_overall_accuracy_best_fit_battery(1702, aggregate_parent = \"",
          gsub("\\\\", "/", project_root),
          "/overall_accuracy_output\")\n",
          "or set Sys.setenv(ATTRACTORS_OVERALL_ACC_AGG_DIR = \"C:/path/to/writable_folder\")\n",
          "then re-source this script.",
          call. = FALSE
        )
      }
    )
    message("\nCombined best-fit summary: ", agg_csv)
    long_csv <- NULL
    if (!is.null(combined_long)) {
      long_csv <- file.path(
        agg_dir,
        paste0("combined_all_models_metrics_long_seed_", seed, ".csv")
      )
      tryCatch(
        write.csv(combined_long, long_csv, row.names = FALSE),
        error = function(e) {
          stop(
            "Cannot write long metrics CSV:\n  ",
            long_csv,
            "\n",
            conditionMessage(e),
            "\n\nUse aggregate_parent= or ATTRACTORS_OVERALL_ACC_AGG_DIR (see combined CSV error hint).",
            call. = FALSE
          )
        }
      )
      message("All run × model metrics (long): ", long_csv)
    }
    if (isTRUE(write_battery_bundle) && length(run_paths) > 0L) {
      bundle_root <- oa_write_battery_bundle(
        agg_dir = agg_dir,
        seed = seed,
        pass_threshold = pass_threshold,
        best_criterion = best_criterion,
        curve_model = curve_model,
        log_root = log_root,
        combined = combined,
        combined_long = combined_long,
        agg_csv = agg_csv,
        long_csv = long_csv,
        run_paths = run_paths
      )
    }
    aggregate_output_dir <- agg_dir
  }

  invisible(list(
    combined = combined,
    combined_all_models_long = combined_long,
    by_run = fit_rows,
    run_paths = run_paths,
    battery_bundle_dir = bundle_root,
    aggregate_output_dir = aggregate_output_dir
  ))
}

# ------------------------------------------------------------------------------
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
    curv <- "aic_best"
    idxc <- match("--curve", args)
    if (!is.na(idxc) && idxc < length(args)) {
      curv <- args[idxc + 1L]
    }
    bestc <- "rmse_pct"
    idxb2 <- match("--best", args)
    if (!is.na(idxb2) && idxb2 < length(args)) {
      bestc <- args[idxb2 + 1L]
    }
    no_b <- !is.na(match("--no-bundle", args))
    agg_par <- NULL
    idxa <- match("--aggregate-parent", args)
    if (!is.na(idxa) && idxa < length(args)) {
      agg_par <- args[idxa + 1L]
    }
    run_overall_accuracy_best_fit_battery(
      seed,
      pass_threshold = pt,
      curve_model = curv,
      best_criterion = bestc,
      write_battery_bundle = !no_b,
      aggregate_parent = agg_par
    )
    quit(save = "no", status = 0L)
  }
  if (length(args) >= 1L) {
    curv <- "aic_best"
    idxc <- match("--curve", args)
    if (!is.na(idxc) && idxc < length(args)) {
      curv <- args[idxc + 1L]
    }
    bestc <- "rmse_pct"
    idxb2 <- match("--best", args)
    if (!is.na(idxb2) && idxb2 < length(args)) {
      bestc <- args[idxb2 + 1L]
    }
    nonopt <- args[!grepl("^--", args)]
    if (length(nonopt) < 1L) {
      stop("Expected path to run folder (not starting with --).")
    }
    run_folder <- nonopt[1L]
    run_overall_accuracy_best_fit(run_folder, curve_model = curv, best_criterion = bestc)
    quit(save = "no", status = 0L)
  }
}
