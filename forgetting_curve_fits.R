# ==============================================================================
# Forgetting curve model fits (linear, exponential, power-law, inverse stage)
# ==============================================================================
# Reads Unity recall_history.csv (same layout as VisualizeForgettingCurves.R):
#   patternId, stage, recallPercent, testNumber
# For each pattern, fits recall ~ stage (cumulative "load") and compares models by AIC.
# Note: AIC for power_loglog is on the log(recall) scale; treat as exploratory vs linear / nls.
#
# Usage (R):
#   setwd("C:/Users/Mak/Attractors")
#   source("forgetting_curve_fits.R")
#   res <- run_forgetting_fits("C:/.../CSVExperimentLogs/radials_neutral_seed1702")
#   print(res$summary)
#
#   # All 2×4 battery runs (radials + geometric20 × neutral, A, B, C) for one seed:
#   run_forgetting_fits_battery(1702)
#   run_forgetting_fits_battery(4242, log_root = "C:/.../CSVExperimentLogs")
#   run_forgetting_fits(..., print_summary = FALSE)  # suppress console best-model table
#
# PowerShell:
#   Rscript forgetting_curve_fits.R "C:/path/to/run_folder"
#   Rscript forgetting_curve_fits.R --battery 1702
# ==============================================================================

# Same layout as experiment_master / postprocess (default 2×4)
FORGETTING_BATTERY_STIMS <- c("radials", "geometric20")
FORGETTING_BATTERY_CONDS <- c("neutral", "condA", "condB", "condC")

default_log_root_forgetting <- function() {
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

resolve_battery_run_folder <- function(log_root, stim, cond, seed) {
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

suppressPackageStartupMessages({
  library(ggplot2)
})

# Optional: drop stacked batteries (same as postprocess / combined overlays)
for (.root in unique(c(getwd(), Sys.getenv("ATTRACTORS_PROJECT")))) {
  if (nzchar(.root) && file.exists(f <- file.path(.root, "experiment_plot_labels.R"))) {
    source(f, local = FALSE)
    break
  }
}
if (!exists("keep_last_cumulative_recall_block", mode = "function", inherits = TRUE)) {
  keep_last_cumulative_recall_block <- function(d) d
}

read_recall_for_fits <- function(folder) {
  f <- file.path(folder, "recall_history.csv")
  if (!file.exists(f)) {
    stop("recall_history.csv not found: ", f)
  }
  d <- read.csv(f, stringsAsFactors = FALSE)
  req <- c("patternId", "stage", "recallPercent")
  if (!all(req %in% names(d))) {
    stop("recall_history.csv must contain: ", paste(req, collapse = ", "))
  }
  d$patternId <- as.character(d$patternId)
  d$stage <- as.integer(d$stage)
  d$recallPercent <- as.numeric(d$recallPercent)
  d <- keep_last_cumulative_recall_block(d)
  d
}

pattern_num <- function(pat) {
  m <- regmatches(pat, regexpr("\\d+", pat))
  if (length(m) == 0L) {
    return(999L)
  }
  as.integer(m[1L])
}

# One row per (patternId, stage): mean recall if duplicated
aggregate_by_stage <- function(d) {
  agg <- aggregate(
    recallPercent ~ patternId + stage,
    data = d,
    FUN = mean,
    na.rm = TRUE
  )
  names(agg)[names(agg) == "recallPercent"] <- "recallPercent"
  agg
}

safe_aic <- function(fit) {
  if (is.null(fit)) {
    return(NA_real_)
  }
  tryCatch(AIC(fit), error = function(...) NA_real_)
}

# --- model fits (return NULL if not identifiable) --------------------------------

fit_linear <- function(d) {
  if (nrow(d) < 3L) {
    return(NULL)
  }
  lm(recallPercent ~ stage, data = d)
}

# log(recall) ~ log(stage): power-law / log-log decay (recall floored slightly > 0)
fit_power_loglog <- function(d) {
  if (nrow(d) < 4L) {
    return(NULL)
  }
  eps <- 1e-2
  d <- d[d$stage > 0, , drop = FALSE]
  if (nrow(d) < 4L) {
    return(NULL)
  }
  d$recallClip <- pmax(d$recallPercent, eps)
  tryCatch(lm(log(recallClip) ~ log(stage), data = d), error = function(...) NULL)
}

# recall ~ a + b/stage
fit_inverse_stage <- function(d) {
  if (nrow(d) < 3L) {
    return(NULL)
  }
  z <- d$stage
  if (any(z <= 0L)) {
    return(NULL)
  }
  tryCatch(lm(recallPercent ~ I(1 / stage), data = d), error = function(...) NULL)
}

# nls SSasympOrig on t = stage - min(stage); attr(fit, "t0") = min(stage) for prediction
fit_exponential_asymp2 <- function(d) {
  if (nrow(d) < 4L) {
    return(NULL)
  }
  d <- d[order(d$stage), ]
  t0 <- min(d$stage)
  t <- d$stage - t0
  y <- d$recallPercent
  fit <- tryCatch(
    nls(y ~ SSasympOrig(t, Asym, lrc, c0)),
    error = function(...) NULL
  )
  if (!is.null(fit)) {
    attr(fit, "t0") <- t0
  }
  fit
}

predict_exp2 <- function(fit, stages) {
  t0 <- attr(fit, "t0")
  if (is.null(t0)) {
    t0 <- 0
  }
  predict(fit, newdata = data.frame(t = stages - t0))
}

predict_best <- function(best, fits, stages) {
  if (is.na(best) || length(stages) == 0L) {
    return(rep(NA_real_, length(stages)))
  }
  out <- tryCatch(
    {
      if (best == "linear" && !is.null(fits$linear)) {
        return(predict(fits$linear, newdata = data.frame(stage = stages)))
      }
      if (best == "exponential_asymp" && !is.null(fits$exponential_asymp)) {
        return(predict_exp2(fits$exponential_asymp, stages))
      }
      if (best == "power_loglog" && !is.null(fits$power_loglog)) {
        nd <- data.frame(stage = stages)
        return(exp(predict(fits$power_loglog, newdata = nd)))
      }
      if (best == "inverse_stage" && !is.null(fits$inverse_stage)) {
        return(predict(fits$inverse_stage, newdata = data.frame(stage = stages)))
      }
      rep(NA_real_, length(stages))
    },
    error = function(...) rep(NA_real_, length(stages))
  )
  ifelse(is.finite(out), pmin(100, pmax(0, out)), NA_real_)
}

fit_all_models <- function(d) {
  d <- aggregate_by_stage(d)
  d <- d[order(d$stage), ]
  list(
    linear = fit_linear(d),
    exponential_asymp = fit_exponential_asymp2(d),
    power_loglog = fit_power_loglog(d),
    inverse_stage = fit_inverse_stage(d),
    data = d
  )
}

model_summary_row <- function(pattern_id, label, fit, n_obs) {
  aic <- safe_aic(fit)
  k <- if (is.null(fit)) {
    NA_integer_
  } else {
    length(coef(fit))
  }
  data.frame(
    patternId = pattern_id,
    model = label,
    n = n_obs,
    k = k,
    AIC = aic,
    stringsAsFactors = FALSE
  )
}

pick_best <- function(rows) {
  rows <- rows[is.finite(rows$AIC), ]
  if (nrow(rows) == 0L) {
    return(NA_character_)
  }
  rows$model[which.min(rows$AIC)]
}

best_model_table <- function(summary_tab) {
  ok <- !is.na(summary_tab$best) & summary_tab$best
  if (!any(ok)) {
    return(NULL)
  }
  st <- summary_tab[ok, , drop = FALSE]
  data.frame(
    patternId = st$patternId,
    best_model = st$model,
    AIC = st$AIC,
    n = st$n,
    stringsAsFactors = FALSE
  )
}

print_forgetting_fit_summary <- function(summary_tab, title = NULL) {
  if (!is.null(title)) {
    message(title)
  }
  bt <- best_model_table(summary_tab)
  if (is.null(bt) || nrow(bt) == 0L) {
    message("(No best model per pattern — all AICs missing or no fits.)")
    return(invisible(NULL))
  }
  print(bt, row.names = FALSE)
  invisible(bt)
}

#' @param folder Path to one Unity experiment folder (contains recall_history.csv).
#' @param out_dir If non-NULL, write summary CSV and optional PNGs here.
#' @param save_plots If TRUE and out_dir set, save one diagnostic plot per pattern.
#' @param print_summary If TRUE, print best model per pattern (by AIC) to the console.
run_forgetting_fits <- function(
    folder,
    out_dir = NULL,
    save_plots = FALSE,
    print_summary = TRUE
) {
  d_all <- read_recall_for_fits(folder)
  pats <- unique(d_all$patternId)
  pats <- pats[order(vapply(pats, pattern_num, integer(1L)))]

  all_rows <- list()
  curves <- list()

  for (p in pats) {
    dp <- d_all[d_all$patternId == p, ]
    fits <- fit_all_models(dp)
    dd <- fits$data
    n_obs <- nrow(dd)
    rows <- rbind(
      model_summary_row(p, "linear", fits$linear, n_obs),
      model_summary_row(p, "exponential_asymp", fits$exponential_asymp, n_obs),
      model_summary_row(p, "power_loglog", fits$power_loglog, n_obs),
      model_summary_row(p, "inverse_stage", fits$inverse_stage, n_obs)
    )
    best_name <- pick_best(rows[is.finite(rows$AIC), , drop = FALSE])
    rows$best <- rows$model == best_name
    all_rows[[length(all_rows) + 1L]] <- rows

    xs <- seq(min(dd$stage), max(dd$stage), length.out = 60L)
    yhat <- predict_best(best_name, fits, xs)
    curve_df <- data.frame(stage = xs, recall_fit = yhat)
    curve_df <- curve_df[is.finite(curve_df$recall_fit), , drop = FALSE]

    plt <- ggplot(dd, aes(x = stage, y = recallPercent)) +
      geom_point(size = 2.5, alpha = 0.9)
    if (nrow(dd) > 1L) {
      plt <- plt + geom_line(alpha = 0.35, linewidth = 0.6)
    }
    if (nrow(curve_df) > 0L) {
      plt <- plt + geom_line(
        data = curve_df,
        aes(x = stage, y = recall_fit),
        color = "steelblue",
        linewidth = 1,
        inherit.aes = FALSE
      )
    }
    plt <- plt +
      labs(
        title = paste0("Forgetting fits: ", p),
        subtitle = paste0(
          "Best AIC model: ",
          ifelse(is.na(best_name), "(none converged)", best_name),
          "  (linear, exponential_asymp, power_loglog, inverse_stage)"
        ),
        x = "Stage (patterns learned)",
        y = "Recall %"
      ) +
      theme_bw() +
      coord_cartesian(ylim = c(0, 105))

    curves[[p]] <- plt

    if (isTRUE(save_plots) && !is.null(out_dir)) {
      if (!dir.exists(out_dir)) {
        dir.create(out_dir, recursive = TRUE)
      }
      fn <- file.path(out_dir, paste0("forgetting_fits_", p, ".png"))
      ggsave(fn, plot = plt, width = 7, height = 4.5, dpi = 150)
      message("Saved ", fn)
    }
  }

  summary_tab <- do.call(rbind, all_rows)
  if (!is.null(out_dir)) {
    if (!dir.exists(out_dir)) {
      dir.create(out_dir, recursive = TRUE)
    }
    sum_path <- file.path(out_dir, "forgetting_curve_model_comparison.csv")
    write.csv(summary_tab, sum_path, row.names = FALSE)
    message("Wrote ", sum_path)
  }

  if (isTRUE(print_summary)) {
    print_forgetting_fit_summary(
      summary_tab,
      title = paste0("Best model per pattern (lowest AIC): ", basename(folder))
    )
  }

  invisible(list(summary = summary_tab, plots = curves, folder = folder))
}

#' Run forgetting fits for every stimulus set × condition folder for one seed.
#' Writes per-run outputs under each folder's rgraphs/forgetting_fits/, and one combined CSV under
#'   <log_root>/_postprocess_figures/forgetting_seed_<seed>/forgetting_curve_model_comparison_all_runs.csv
run_forgetting_fits_battery <- function(
    seed,
    log_root = NULL,
    stim_sets = FORGETTING_BATTERY_STIMS,
    conds = FORGETTING_BATTERY_CONDS,
    save_plots = TRUE,
    print_summary = TRUE
) {
  if (is.null(log_root)) {
    log_root <- default_log_root_forgetting()
  } else {
    log_root <- normalizePath(log_root, winslash = "/", mustWork = FALSE)
  }
  if (!dir.exists(log_root)) {
    stop("log_root not found: ", log_root)
  }

  summaries <- list()
  for (stim in stim_sets) {
    for (cond in conds) {
      folder <- resolve_battery_run_folder(log_root, stim, cond, seed)
      if (is.na(folder) || !nzchar(folder)) {
        warning(
          "Skipping ", stim, "_", cond, "_seed", seed,
          " — no recall_history.csv (canonical or double-seed folder).",
          call. = FALSE,
          immediate. = TRUE
        )
        next
      }
      message("\n=== ", basename(folder), " ===")
      out <- file.path(folder, "rgraphs", "forgetting_fits")
      res <- run_forgetting_fits(
        folder,
        out_dir = out,
        save_plots = save_plots,
        print_summary = print_summary
      )
      summaries[[basename(folder)]] <- res$summary
    }
  }

  big <- if (length(summaries) > 0L) {
    do.call(rbind, summaries)
  } else {
    NULL
  }
  if (!is.null(big)) {
    agg_dir <- file.path(log_root, "_postprocess_figures", paste0("forgetting_seed_", seed))
    dir.create(agg_dir, recursive = TRUE, showWarnings = FALSE)
    agg_csv <- file.path(agg_dir, "forgetting_curve_model_comparison_all_runs.csv")
    write.csv(big, agg_csv, row.names = FALSE)
    message("\nCombined summary (all conditions × sets): ", agg_csv)
  }

  invisible(list(by_run = summaries, combined = big))
}

# ------------------------------------------------------------------------------
if (!interactive()) {
  args <- commandArgs(trailingOnly = TRUE)
  idxb <- match("--battery", args)
  if (!is.na(idxb) && idxb < length(args)) {
    run_forgetting_fits_battery(args[idxb + 1L])
    quit(save = "no", status = 0L)
  }
  if (length(args) >= 1L) {
    folder <- args[1L]
    out <- file.path(folder, "rgraphs", "forgetting_fits")
    run_forgetting_fits(folder, out_dir = out, save_plots = TRUE)
  }
}
