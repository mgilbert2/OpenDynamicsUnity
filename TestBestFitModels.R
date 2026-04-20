suppressPackageStartupMessages(library(ggplot2))

INCLUDE_STEPFUNCTION <- FALSE
INCLUDE_PIECEWISE_LINEAR <- FALSE

CONDITION <- "geometric20_condA_seed1702"
DATA_ROOT <- "/Users/makenzygilbert/OpenDynamicsUnity/seed1702_inputs"
DEFAULT_SEED <- 1702
ALL_STIMS <- c("radials", "geometric20")
ALL_CONDS <- c("neutral", "condA", "condB", "condC")
# Use 90.5 to match your existing overall-accuracy plot outputs.
PASS_THRESHOLD <- 90.5

# "accuracy_pct" = percentage recalled at each stage (matches your OA graphs).
# "passed_count" = number of patterns recalled at each stage.
RESPONSE_METRIC <- "accuracy_pct"
# Restrict each stage to pattern IDs present at the final stage (avoids inflated denominators).
FILTER_TO_FINAL_PATTERN_SET <- TRUE


clip_pct <- function(x) {
  pmin(100, pmax(0, x))
}

build_condition_names <- function(
    seed = DEFAULT_SEED,
    stims = ALL_STIMS,
    conds = ALL_CONDS) {
  as.vector(unlist(lapply(stims, function(st) paste0(st, "_", conds, "_seed", seed))))
}

make_datasets <- function(seed = 1702L) {
  set.seed(seed)
  stage <- 1:10

  dataset1 <- data.frame(
    stage = stage,
    accuracy = clip_pct(92 - 2.2 * stage + rnorm(length(stage), 0, 1.8))
  )

  dataset2 <- data.frame(
    stage = stage,
    accuracy = clip_pct(94 - 1.6 * stage - 2.2 * pmax(0, stage - 10) + rnorm(length(stage), 0, 1.6))
  )

  dataset3 <- data.frame(
    stage = stage,
    accuracy = clip_pct(20 + 78 / (1 + exp((stage - 9) / 1.9)) + rnorm(length(stage), 0, 1.7))
  )

  dataset4 <- data.frame(
    stage = stage,
    accuracy = clip_pct(ifelse(stage <= 7, 88, ifelse(stage <= 13, 66, 43)) + rnorm(length(stage), 0, 2.0))
  )

  dataset5 <- data.frame(
    stage = stage,
    accuracy = clip_pct(94 * stage^(-0.34) + rnorm(length(stage), 0, 1.4))
  )

  dataset6 <- data.frame(
    stage = stage,
    accuracy = clip_pct(18 + 83 / stage + rnorm(length(stage), 0, 1.4))
  )

  dataset7 <- data.frame(
    stage = stage,
    accuracy = clip_pct(91 - 1.1 * stage - 14 / (1 + exp(-(stage - 12) / 1.8)) + rnorm(length(stage), 0, 2.0))
  )

  out <- list(
    dataset1 = dataset1,
    dataset2 = dataset2,
    dataset3 = dataset3,
    dataset4 = dataset4,
    dataset5 = dataset5,
    dataset6 = dataset6,
    dataset7 = dataset7
  )

  for (nm in names(out)) {
    assign(nm, out[[nm]], envir = .GlobalEnv)
  }
  assign("datasets", out, envir = .GlobalEnv)
  invisible(out)
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

compute_overall_accuracy_from_recalls_local <- function(
    recall_data,
    pass_threshold = PASS_THRESHOLD,
    filter_to_final_pattern_set = FILTER_TO_FINAL_PATTERN_SET) {
  if (is.null(recall_data) || nrow(recall_data) == 0L) {
    stop("recall data is empty.")
  }
  all_stages <- sort(unique(recall_data$stage))
  final_stage <- max(all_stages)
  final_stage_ids <- unique(recall_data$patternId[recall_data$stage == final_stage])
  accuracy_data <- data.frame(
    stage = integer(),
    totalPatterns = integer(),
    passedPatterns = integer(),
    accuracyPercent = numeric(),
    stringsAsFactors = FALSE
  )
  for (stage in all_stages) {
    stage_data <- recall_data[recall_data$stage == stage, , drop = FALSE]
    if (isTRUE(filter_to_final_pattern_set)) {
      stage_data <- stage_data[stage_data$patternId %in% final_stage_ids, , drop = FALSE]
    }
    if (nrow(stage_data) == 0L) {
      next
    }
    stage_patterns <- unique(stage_data$patternId)
    pattern_recalls <- data.frame(
      patternId = character(length(stage_patterns)),
      recallPercent = numeric(length(stage_patterns)),
      stringsAsFactors = FALSE
    )
    for (j in seq_along(stage_patterns)) {
      pat_id <- stage_patterns[j]
      pat_data <- stage_data[stage_data$patternId == pat_id, , drop = FALSE]
      pattern_recalls$patternId[j] <- pat_id
      pattern_recalls$recallPercent[j] <- pat_data$recallPercent[nrow(pat_data)]
    }
    total_patterns <- nrow(pattern_recalls)
    passed_patterns <- sum(pattern_recalls$recallPercent >= pass_threshold)
    accuracy_percent <- (passed_patterns / total_patterns) * 100
    accuracy_data <- rbind(
      accuracy_data,
      data.frame(
        stage = stage,
        totalPatterns = total_patterns,
        passedPatterns = passed_patterns,
        accuracyPercent = accuracy_percent,
        stringsAsFactors = FALSE
      )
    )
  }
  accuracy_data
}

stage_accuracy_table <- function(
    recall_data,
    pass_threshold = PASS_THRESHOLD,
    filter_to_final_pattern_set = FILTER_TO_FINAL_PATTERN_SET) {
  out <- compute_overall_accuracy_from_recalls_local(
    recall_data,
    pass_threshold = pass_threshold,
    filter_to_final_pattern_set = filter_to_final_pattern_set
  )
  out$failedPatterns <- out$totalPatterns - out$passedPatterns
  out$accuracy <- out$accuracyPercent
  out
}

get_condition_data <- function(
    condition = CONDITION,
    data_root = DATA_ROOT,
    pass_threshold = PASS_THRESHOLD,
    response_metric = RESPONSE_METRIC,
    filter_to_final_pattern_set = FILTER_TO_FINAL_PATTERN_SET) {
  run_folder <- normalizePath(file.path(data_root, condition), winslash = "/", mustWork = TRUE)
  recall <- read_recall_history(run_folder)
  acc <- stage_accuracy_table(
    recall,
    pass_threshold = pass_threshold,
    filter_to_final_pattern_set = filter_to_final_pattern_set
  )
  if (nrow(acc) < 2L) {
    stop("Need at least 2 stages in: ", run_folder)
  }
  acc <- acc[order(acc$stage), , drop = FALSE]
  response_metric <- match.arg(response_metric, c("passed_count", "accuracy_pct"))
  if (identical(response_metric, "passed_count")) {
    acc$accuracy <- acc$passedPatterns
    y_label <- "Patterns recalled correctly (#)"
    y_max <- max(acc$totalPatterns, na.rm = TRUE)
  } else {
    acc$accuracy <- acc$accuracy
    y_label <- "Accuracy (%)"
    y_max <- 100
  }
  attr(acc, "run_folder") <- run_folder
  attr(acc, "condition") <- condition
  attr(acc, "response_metric") <- response_metric
  attr(acc, "y_label") <- y_label
  attr(acc, "y_max") <- y_max
  attr(acc, "filter_to_final_pattern_set") <- filter_to_final_pattern_set
  acc
}

fit_linear <- function(d) {
  tryCatch(lm(accuracy ~ stage, data = d), error = function(...) NULL)
}

fit_hyperbolic_decay <- function(d) {
  if (any(d$stage <= 0)) {
    return(NULL)
  }
  tryCatch(lm(accuracy ~ I(1 / stage), data = d), error = function(...) NULL)
}

fit_stepfunction <- function(d) {
  if (nrow(d) < 4L) {
    return(NULL)
  }
  s <- sort(unique(d$stage))
  if (length(s) < 4L) {
    return(NULL)
  }
  # Exactly one step: split stages into early vs late at one changepoint.
  k_candidates <- s[2:(length(s) - 1)]
  best_fit <- NULL
  best_aic <- Inf
  for (k in k_candidates) {
    fit_k <- tryCatch(
      lm(accuracy ~ I(stage > k), data = d),
      error = function(...) NULL
    )
    if (!is.null(fit_k)) {
      aic_k <- tryCatch(as.numeric(AIC(fit_k)), error = function(...) Inf)
      if (is.finite(aic_k) && aic_k < best_aic) {
        best_aic <- aic_k
        best_fit <- fit_k
        attr(best_fit, "knot") <- k
      }
    }
  }
  best_fit
}

fit_piecewise_linear <- function(d) {
  if (nrow(d) < 6L) {
    return(NULL)
  }
  k_candidates <- sort(unique(d$stage))[3:(length(unique(d$stage)) - 2)]
  best_fit <- NULL
  best_aic <- Inf
  for (k in k_candidates) {
    fit_k <- tryCatch(
      lm(accuracy ~ stage + pmax(0, stage - k), data = d),
      error = function(...) NULL
    )
    if (!is.null(fit_k)) {
      aic_k <- tryCatch(as.numeric(AIC(fit_k)), error = function(...) Inf)
      if (is.finite(aic_k) && aic_k < best_aic) {
        best_aic <- aic_k
        best_fit <- fit_k
        attr(best_fit, "knot") <- k
      }
    }
  }
  best_fit
}

fit_sigmoid <- function(d) {
  if (nrow(d) < 6L) {
    return(NULL)
  }
  tryCatch(
    nls(accuracy ~ SSfpl(stage, A, B, xmid, scal), data = d, control = list(maxiter = 200)),
    error = function(...) NULL
  )
}

fit_power_law <- function(d) {
  if (any(d$stage <= 0) || nrow(d) < 4L) {
    return(NULL)
  }
  starts <- list(
    list(a = max(d$accuracy, na.rm = TRUE), b = -0.3),
    list(a = mean(d$accuracy, na.rm = TRUE) * 1.2, b = -0.5),
    list(a = d$accuracy[1], b = -0.2)
  )
  for (st in starts) {
    fit <- tryCatch(
      nls(accuracy ~ a * stage^b, data = d, start = st, control = list(maxiter = 200)),
      error = function(...) NULL
    )
    if (!is.null(fit)) {
      return(fit)
    }
  }
  NULL
}

safe_aic <- function(fit) {
  if (is.null(fit)) {
    return(NA_real_)
  }
  tryCatch(as.numeric(AIC(fit)), error = function(...) NA_real_)
}

predict_fit <- function(model_name, fit, stages, d = NULL) {
  if (is.null(fit)) {
    return(rep(NA_real_, length(stages)))
  }
  if (identical(model_name, "stepfunction")) {
    if (is.null(d) || nrow(d) == 0L) {
      return(rep(NA_real_, length(stages)))
    }
    # Predict stepwise by carrying forward the fitted value of the most recent stage.
    d_ord <- d[order(d$stage), , drop = FALSE]
    stage_u <- sort(unique(d_ord$stage))
    fitted_u <- as.numeric(tapply(fitted(fit), d_ord$stage, mean)[as.character(stage_u)])
    idx <- pmax(1L, findInterval(stages, stage_u))
    idx[stages > max(stage_u)] <- length(stage_u)
    return(clip_pct(fitted_u[idx]))
  }
  yhat <- tryCatch(
    predict(fit, newdata = data.frame(stage = stages)),
    error = function(...) rep(NA_real_, length(stages))
  )
  clip_pct(as.numeric(yhat))
}

fit_all_models <- function(
    d,
    include_stepfunction = INCLUDE_STEPFUNCTION,
    include_piecewise_linear = INCLUDE_PIECEWISE_LINEAR) {
  fits <- list(
    linear = fit_linear(d),
    sigmoid = fit_sigmoid(d),
    power_law = fit_power_law(d)
  )
  if (isTRUE(include_piecewise_linear)) {
    fits$piecewise_linear <- fit_piecewise_linear(d)
  }
  if (isTRUE(include_stepfunction)) {
    fits$stepfunction <- fit_stepfunction(d)
  }

  cmp <- data.frame(
    model = names(fits),
    AIC = vapply(fits, safe_aic, numeric(1)),
    stringsAsFactors = FALSE
  )
  cmp <- cmp[order(cmp$AIC, na.last = TRUE), , drop = FALSE]
  best_model <- cmp$model[which.min(cmp$AIC)]

  list(fits = fits, comparison = cmp, best_model = best_model)
}

plot_dataset_fits <- function(
    dataset = 1L,
    data_list = NULL,
    include_stepfunction = INCLUDE_STEPFUNCTION,
    include_piecewise_linear = INCLUDE_PIECEWISE_LINEAR) {
  if (is.null(data_list)) {
    if (exists("datasets", envir = .GlobalEnv, inherits = FALSE)) {
      data_list <- get("datasets", envir = .GlobalEnv, inherits = FALSE)
    } else {
      data_list <- make_datasets()
    }
  }

  ds_name <- if (is.numeric(dataset)) {
    paste0("dataset", as.integer(dataset))
  } else {
    as.character(dataset)[1L]
  }

  if (!ds_name %in% names(data_list)) {
    stop("Unknown dataset: ", ds_name)
  }

  d <- data_list[[ds_name]]
  fit_res <- fit_all_models(
    d,
    include_stepfunction = include_stepfunction,
    include_piecewise_linear = include_piecewise_linear
  )

  xs <- seq(min(d$stage), max(d$stage), length.out = 200)
  curve_df <- do.call(
    rbind,
    lapply(names(fit_res$fits), function(m) {
      data.frame(
        stage = xs,
        accuracy = predict_fit(m, fit_res$fits[[m]], xs, d = d),
        model = m,
        is_best = m == fit_res$best_model,
        stringsAsFactors = FALSE
      )
    })
  )
  curve_df <- curve_df[is.finite(curve_df$accuracy), , drop = FALSE]

  p <- ggplot(d, aes(stage, accuracy)) +
    geom_point(size = 2.4, color = "black") +
    geom_line(linewidth = 0.65, color = "black", alpha = 0.7) +
    geom_line(
      data = curve_df[!curve_df$is_best, , drop = FALSE],
      aes(stage, accuracy, group = model),
      color = "grey75",
      linewidth = 0.7,
      inherit.aes = FALSE
    ) +
    geom_line(
      data = curve_df[curve_df$is_best, , drop = FALSE],
      aes(stage, accuracy),
      color = "red",
      linewidth = 0.95,
      inherit.aes = FALSE
    ) +
    coord_cartesian(ylim = c(0, 100)) +
    labs(
      title = paste0(ds_name, " - Best Fit by AIC"),
      subtitle = paste0("Best model: ", fit_res$best_model),
      x = "Stage",
      y = "Accuracy (%)"
    ) +
    theme_minimal()

  print(p)
  print(fit_res$comparison)

  invisible(list(
    dataset_name = ds_name,
    data = d,
    comparison = fit_res$comparison,
    best_model = fit_res$best_model,
    plot = p
  ))
}

plot_condition_fits <- function(
    condition = CONDITION,
    data_root = DATA_ROOT,
    pass_threshold = PASS_THRESHOLD,
    response_metric = RESPONSE_METRIC,
    filter_to_final_pattern_set = FILTER_TO_FINAL_PATTERN_SET,
    include_stepfunction = INCLUDE_STEPFUNCTION,
    include_piecewise_linear = INCLUDE_PIECEWISE_LINEAR) {
  d <- get_condition_data(
    condition = condition,
    data_root = data_root,
    pass_threshold = pass_threshold,
    response_metric = response_metric,
    filter_to_final_pattern_set = filter_to_final_pattern_set
  )
  fit_res <- fit_all_models(
    d,
    include_stepfunction = include_stepfunction,
    include_piecewise_linear = include_piecewise_linear
  )

  xs <- seq(min(d$stage), max(d$stage), length.out = 200)
  curve_df <- do.call(
    rbind,
    lapply(names(fit_res$fits), function(m) {
      data.frame(
        stage = xs,
        accuracy = predict_fit(m, fit_res$fits[[m]], xs, d = d),
        model = m,
        is_best = m == fit_res$best_model,
        stringsAsFactors = FALSE
      )
    })
  )
  curve_df <- curve_df[is.finite(curve_df$accuracy), , drop = FALSE]

  p <- ggplot(d, aes(stage, accuracy)) +
    geom_point(size = 2.4, color = "black") +
    geom_line(linewidth = 0.65, color = "black", alpha = 0.7) +
    geom_text(
      aes(label = paste0(passedPatterns, "/", totalPatterns)),
      vjust = -0.8,
      size = 3.2,
      color = "gray45"
    ) +
    geom_line(
      data = curve_df[!curve_df$is_best, , drop = FALSE],
      aes(stage, accuracy, group = model),
      color = "grey75",
      linewidth = 0.7,
      inherit.aes = FALSE
    ) +
    geom_line(
      data = curve_df[curve_df$is_best, , drop = FALSE],
      aes(stage, accuracy),
      color = "red",
      linewidth = 0.95,
      inherit.aes = FALSE
    ) +
    coord_cartesian(ylim = c(0, attr(d, "y_max"))) +
    labs(
      title = paste0(condition, " - Best Fit by AIC"),
      subtitle = paste0("Best model: ", fit_res$best_model),
      x = "Stage",
      y = attr(d, "y_label")
    ) +
    theme_minimal()

  print(p)
  print(fit_res$comparison)

  invisible(list(
    condition = condition,
    run_folder = attr(d, "run_folder"),
    data = d,
    comparison = fit_res$comparison,
    best_model = fit_res$best_model,
    plot = p
  ))
}

plot_all_conditions <- function(
    seed = DEFAULT_SEED,
    stims = ALL_STIMS,
    conds = ALL_CONDS,
    data_root = DATA_ROOT,
    pass_threshold = PASS_THRESHOLD,
    response_metric = RESPONSE_METRIC,
    filter_to_final_pattern_set = FILTER_TO_FINAL_PATTERN_SET,
    include_stepfunction = INCLUDE_STEPFUNCTION,
    include_piecewise_linear = INCLUDE_PIECEWISE_LINEAR,
    stop_on_missing = FALSE) {
  condition_names <- build_condition_names(seed = seed, stims = stims, conds = conds)
  out <- list()
  best_rows <- data.frame(
    condition = character(),
    best_model = character(),
    best_aic = numeric(),
    stringsAsFactors = FALSE
  )

  for (cond in condition_names) {
    res <- tryCatch(
      plot_condition_fits(
        condition = cond,
        data_root = data_root,
        pass_threshold = pass_threshold,
        response_metric = response_metric,
        filter_to_final_pattern_set = filter_to_final_pattern_set,
        include_stepfunction = include_stepfunction,
        include_piecewise_linear = include_piecewise_linear
      ),
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
      out[[cond]] <- res
      best_aic <- res$comparison$AIC[1L]
      best_rows <- rbind(
        best_rows,
        data.frame(
          condition = cond,
          best_model = res$best_model,
          best_aic = best_aic,
          stringsAsFactors = FALSE
        )
      )
    }
  }

  if (nrow(best_rows) > 0L) {
    message("\nBest model by condition:")
    print(best_rows)
  }
  invisible(list(results = out, best_summary = best_rows))
}

run_all_datasets <- function(
    data_list = NULL,
    include_stepfunction = INCLUDE_STEPFUNCTION,
    include_piecewise_linear = INCLUDE_PIECEWISE_LINEAR) {
  if (is.null(data_list)) {
    if (exists("datasets", envir = .GlobalEnv, inherits = FALSE)) {
      data_list <- get("datasets", envir = .GlobalEnv, inherits = FALSE)
    } else {
      data_list <- make_datasets()
    }
  }
  out <- lapply(
    seq_along(data_list),
    function(i) plot_dataset_fits(
      i,
      data_list = data_list,
      include_stepfunction = include_stepfunction,
      include_piecewise_linear = include_piecewise_linear
    )
  )
  names(out) <- names(data_list)
  invisible(out)
}

# Synthetic section is available but not auto-run. Uncomment if needed:
# make_datasets(seed = 1702)
# plot_dataset_fits(1)

# Real-data usage (recommended):
# 1) Set CONDITION/DATA_ROOT/PASS_THRESHOLD at the top of this file.
# 2) source("TestBestFitModels.R")
# 3) plot_condition_fits()
# 4) plot_all_conditions()    # runs all 8 standard conditions for DEFAULT_SEED
