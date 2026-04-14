# ------------------------------------------------------------------------------
# Extended overall-accuracy fits: same 4 models as overall_accuracy_best_fit.R,
# plus sigmoid (SSfpl) and step (one level per stage). Picks the "best" among
# the first four + sigmoid (step is never the winner — it would always win RMSE).
#
# How to run (set path to your Unity run folder):
#   setwd("C:/Users/Mak/Attractors")
#   source("overall_accuracy_single_condition_extended.R")
#   run_overall_accuracy_single_cell_extended(
#     "C:/Users/.../CSVExperimentLogs/radials_neutral_seed1702",
#     pass_threshold = 90.5
#   )
# One condition, both stims:
#   run_overall_accuracy_one_condition_extended(1702, cond = "neutral")
#
# Rscript:  ... --seed 1702 --cond neutral   |   ... "C:/path/to/run_folder"
#   Add --no-plot to skip drawing.  Add --pass 90.5 to change threshold.
# ------------------------------------------------------------------------------

suppressPackageStartupMessages(library(ggplot2))

# Where overall_accuracy_best_fit.R lives (same folder as this script, or ATTRACTORS_PROJECT)
project_root <- Sys.getenv("ATTRACTORS_PROJECT", unset = "")
if (!nzchar(project_root)) {
  dd <- commandArgs(trailingOnly = FALSE)
  p <- sub("^--file=", "", dd[grepl("^--file=", dd)][1L])
  project_root <- if (!is.na(p) && nzchar(p)) {
    dirname(normalizePath(p, winslash = "/", mustWork = TRUE))
  } else {
    normalizePath(getwd(), winslash = "/", mustWork = FALSE)
  }
}
project_root <- normalizePath(project_root, winslash = "/", mustWork = FALSE)

src_oa <- file.path(project_root, "overall_accuracy_best_fit.R")
if (!file.exists(src_oa)) {
  stop("Need overall_accuracy_best_fit.R at:\n  ", src_oa)
}
source(src_oa, local = FALSE)

# Models allowed to "win" best fit (step_saturated is left out on purpose)
FAIR <- c("linear", "exponential_asymp", "power_loglog", "inverse_stage", "sigmoid_ssfpl")

# ------------------------------------------------------------------------------
# Step fit — kept at top level so overall_accuracy_step_only_battery.R can use it
# ------------------------------------------------------------------------------
oa_fit_step_saturated <- function(d) {
  if (nrow(d) < 2L) {
    return(NULL)
  }
  d <- d[order(d$stage), , drop = FALSE]
  tryCatch(lm(accuracyPercent ~ factor(stage), data = d), error = function(e) NULL)
}

# Predict accuracy % at vector `stages` for one model name (handles sigmoid + step + rest)
predict_accuracy <- function(model_name, fits, train, stages) {
  clip01 <- function(z) {
    ifelse(is.finite(z), pmin(100, pmax(0, z)), NA_real_)
  }
  if (model_name == "sigmoid_ssfpl" && !is.null(fits$sigmoid_ssfpl)) {
    return(clip01(predict(fits$sigmoid_ssfpl, newdata = data.frame(stage = stages))))
  }
  if (model_name == "step_saturated" && !is.null(fits$step_saturated)) {
    p <- predict(fits$step_saturated, newdata = data.frame(stage = stages))
    if (!anyNA(p)) {
      return(pmin(100, pmax(0, p)))
    }
    # New stages not in training: carry last fitted value to the left
    tr <- train[order(train$stage), , drop = FALSE]
    fv <- fitted(fits$step_saturated)[order(train$stage)]
    ts <- tr$stage
    for (i in seq_along(stages)) {
      if (is.na(p[i])) {
        w <- which(ts <= stages[i])
        p[i] <- if (length(w)) fv[max(w)] else fv[1L]
      }
    }
    return(pmin(100, pmax(0, p)))
  }
  clip01(oa_predict_best(model_name, fits, stages))
}

# rbind() needs the same columns — copy missing columns from `template` into `extra`
pad_rows_like <- function(extra, template) {
  for (nm in setdiff(names(template), names(extra))) {
    v <- template[[nm]]
    extra[[nm]] <- rep(
      if (is.logical(v)) {
        FALSE
      } else if (is.integer(v)) {
        NA_integer_
      } else if (is.character(v)) {
        NA_character_
      } else {
        NA_real_
      },
      nrow(extra)
    )
  }
  extra[, names(template), drop = FALSE]
}

# Fit sigmoid + step, add them to the comparison table, recompute RMSE / flags
fit_all_models <- function(accuracy_data) {
  base <- oa_compare_models(accuracy_data)
  d <- base$data
  fits <- base$fits

  fits$sigmoid_ssfpl <- if (nrow(d) >= 5L) {
    tryCatch(
      nls(accuracyPercent ~ SSfpl(stage, A, B, xmid, scal), data = d, control = list(maxiter = 200)),
      error = function(e) NULL
    )
  } else {
    NULL
  }
  fits$step_saturated <- oa_fit_step_saturated(d)

  extra <- rbind(
    oa_model_row("sigmoid_ssfpl", fits$sigmoid_ssfpl, nrow(d)),
    oa_model_row("step_saturated", fits$step_saturated, nrow(d))
  )
  comp <- rbind(base$comparison, pad_rows_like(extra, base$comparison))

  st <- d$stage
  y <- d$accuracyPercent
  for (i in seq_len(nrow(comp))) {
    yhat <- predict_accuracy(comp$model[i], fits, d, st)
    m <- oa_pct_residual_metrics(y, yhat)
    comp$RMSE_pct[i] <- m$RMSE_pct
    comp$MAE_pct[i] <- m$MAE_pct
    comp$R2_pct[i] <- m$R2_pct
  }

  comp$best_by_aic_all <- comp$model == oa_pick_best_model(comp)
  fair <- comp[comp$model %in% FAIR & is.finite(comp$RMSE_pct), , drop = FALSE]
  best_rmse <- fair$model[which.min(fair$RMSE_pct)]
  comp$best_by_rmse_pct <- comp$model == best_rmse
  comp$best_by_aic_comparable <- comp$model == oa_pick_best_aic_comparable(
    comp[comp$model %in% OA_AIC_COMPARABLE_MODELS, , drop = FALSE]
  )

  list(fits = fits, comparison = comp, data = d, best_name = best_rmse)
}

make_plot <- function(acc, folder_name, pass_threshold, result, show_extra, show_pass_line) {
  best <- result$best_name
  d <- result$data
  xs <- seq(min(d$stage), max(d$stage), length.out = 80L)
  curve <- data.frame(stage = xs, yhat = predict_accuracy(best, result$fits, d, xs))
  curve <- curve[is.finite(curve$yhat), , drop = FALSE]

  sub1 <- if (show_pass_line) {
    paste0("\nPass ≥ ", pass_threshold, "% recall per pattern")
  } else {
    ""
  }
  sub2 <- if (show_extra) {
    paste0(sub1, "\nRed = best (fair): ", best, "  ·  orange = sigmoid  ·  gray = step")
  } else {
    paste0(sub1, "\nRed = best (fair): ", best)
  }

  p <- ggplot(acc, aes(stage, accuracyPercent)) +
    geom_line(linewidth = 1.5, color = "steelblue", alpha = 0.8) +
    geom_point(size = 4, color = "steelblue") +
    geom_text(aes(label = paste0(passedPatterns, "/", totalPatterns)), vjust = -1.1, size = 3.5, color = "gray40") +
    geom_line(data = curve, aes(stage, yhat), color = "firebrick", linewidth = 1, inherit.aes = FALSE) +
    labs(title = "Overall accuracy", subtitle = paste0(folder_name, sub2), x = "Stage", y = "Accuracy (%)") +
    theme_minimal() +
    scale_x_continuous(breaks = acc$stage) +
    scale_y_continuous(limits = c(0, 100), breaks = seq(0, 100, 20)) +
    geom_hline(yintercept = 100, linetype = "dashed", color = "gray80")

  if (show_extra) {
    if (!is.null(result$fits$sigmoid_ssfpl)) {
      xs2 <- seq(min(d$stage), max(d$stage), length.out = 80L)
      sig <- data.frame(stage = xs2, yhat = predict_accuracy("sigmoid_ssfpl", result$fits, d, xs2))
      sig <- sig[is.finite(sig$yhat), , drop = FALSE]
      if (nrow(sig) > 1L) {
        p <- p + geom_line(data = sig, aes(stage, yhat), color = "darkorange", linetype = 2, linewidth = 0.9, inherit.aes = FALSE)
      }
    }
    if (!is.null(result$fits$step_saturated)) {
      ust <- sort(unique(d$stage))
      stp <- data.frame(stage = ust, yhat = predict_accuracy("step_saturated", result$fits, d, ust))
      p <- p + geom_step(data = stp, aes(stage, yhat), direction = "hv", color = "gray40", linewidth = 0.6, inherit.aes = FALSE)
    }
  }
  p
}

# ------------------------------------------------------------------------------
# One Unity run folder
# ------------------------------------------------------------------------------
run_overall_accuracy_single_cell_extended <- function(
    folder,
    pass_threshold = 80.0,
    save_outputs = TRUE,
    plot_width = 12,
    plot_height = 7,
    show_plot = TRUE,
    show_sigmoid_and_step_on_plot = FALSE,
    show_pass_line_in_subtitle = TRUE
) {
  folder <- normalizePath(path.expand(folder), winslash = "/", mustWork = TRUE)
  recall <- readRecallHistory(folder, quiet = TRUE)
  if (is.null(recall) || nrow(recall) == 0L) {
    stop("Missing or empty recall_history.csv in:\n  ", folder)
  }
  acc <- compute_overall_accuracy_from_recalls(recall, pass_threshold, verbose = FALSE)
  if (nrow(acc) == 0L) {
    stop("No accuracy rows for:\n  ", folder)
  }

  result <- fit_all_models(acc)
  message("Best model (lowest RMSE among fair set): ", result$best_name)
  print(result$comparison[, c("model", "RMSE_pct", "AIC", "best_by_rmse_pct")])

  p <- make_plot(acc, basename(folder), pass_threshold, result, show_sigmoid_and_step_on_plot, show_pass_line_in_subtitle)
  if (show_plot) {
    print(p)
  }

  out_dir <- file.path(folder, "rgraphs", "overall_accuracy_single_condition_extended")
  if (save_outputs) {
    dir.create(out_dir, recursive = TRUE, showWarnings = FALSE)
    ggsave(file.path(out_dir, "overall_accuracy_single_condition_extended.png"), p, width = plot_width, height = plot_height, dpi = 300)
    write.csv(acc, file.path(out_dir, "overall_accuracy_stage_table.csv"), row.names = FALSE)
    write.csv(result$comparison, file.path(out_dir, "overall_accuracy_model_comparison_extended.csv"), row.names = FALSE)
    message("Saved under ", out_dir)
  }

  invisible(list(accuracy = acc, comparison = result$comparison, best_model = result$best_name, fits = result$fits, plot = p))
}

# ------------------------------------------------------------------------------
# One seed + one condition (neutral / condA / condB / condC), default both stims
# ------------------------------------------------------------------------------
run_overall_accuracy_one_condition_extended <- function(
    seed,
    cond,
    log_root = NULL,
    pass_threshold = 80.0,
    stim_sets = OVERALL_ACC_BATTERY_STIMS,
    show_plot = TRUE,
    show_sigmoid_and_step_on_plot = FALSE,
    save_outputs = TRUE
) {
  cond <- match.arg(cond, OVERALL_ACC_BATTERY_CONDS)
  if (is.null(log_root)) {
    log_root <- default_log_root_overall_acc()
  }
  log_root <- normalizePath(log_root, winslash = "/", mustWork = FALSE)
  if (!dir.exists(log_root)) {
    stop("log_root not found: ", log_root)
  }

  out <- list()
  for (stim in stim_sets) {
    f <- resolve_battery_run_folder_overall(log_root, stim, cond, seed)
    if (is.na(f) || !nzchar(f)) {
      warning("Skip (no recall): ", stim, "_", cond, call. = FALSE, immediate. = TRUE)
      next
    }
    message("\n=== ", basename(f), " ===")
    out[[basename(f)]] <- run_overall_accuracy_single_cell_extended(
      f,
      pass_threshold = pass_threshold,
      save_outputs = save_outputs,
      show_plot = show_plot,
      show_sigmoid_and_step_on_plot = show_sigmoid_and_step_on_plot
    )
  }
  invisible(out)
}

# ------------------------------------------------------------------------------
# Command line (optional)
# ------------------------------------------------------------------------------
if (!interactive()) {
  args <- commandArgs(trailingOnly = TRUE)
  no_plot <- "--no-plot" %in% args

  i <- match("--seed", args)
  j <- match("--cond", args)
  if (!is.na(i) && !is.na(j) && i < length(args) && j < length(args)) {
    seed <- args[i + 1L]
    cond <- args[j + 1L]
    pt <- 80.0
    k <- match("--pass", args)
    if (!is.na(k) && k < length(args)) {
      pt <- as.numeric(args[k + 1L])
    }
    lr <- NULL
    k <- match("--log-root", args)
    if (!is.na(k) && k < length(args)) {
      lr <- args[k + 1L]
    }
    run_overall_accuracy_one_condition_extended(seed, cond, log_root = lr, pass_threshold = pt, show_plot = !no_plot)
    quit(save = "no", status = 0L)
  }

  plain <- args[!grepl("^--", args)]
  if (length(plain) >= 1L) {
    pt <- 80.0
    k <- match("--pass", args)
    if (!is.na(k) && k < length(args)) {
      pt <- as.numeric(args[k + 1L])
    }
    run_overall_accuracy_single_cell_extended(plain[1L], pass_threshold = pt, show_plot = !no_plot)
    quit(save = "no", status = 0L)
  }
}
