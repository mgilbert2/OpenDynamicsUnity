# Bestfitanalysis.R — minimal sanity check for ONE run folder.
# To run a different condition: point RUN_FOLDER at that condition's folder
# (e.g., .../radials_condA_seed1702 vs .../radials_neutral_seed1702),
# or pass the folder as the first Rscript argument.

suppressPackageStartupMessages(library(ggplot2))

read_recall_history <- function(folder) {
  f <- file.path(folder, "recall_history.csv")
  if (!file.exists(f)) {
    return(NULL)
  }
  d <- read.csv(f, stringsAsFactors = FALSE)
  if (!all(c("patternId", "stage", "recallPercent") %in% names(d))) {
    return(NULL)
  }
  d$patternId <- as.character(d$patternId)
  d$stage <- as.integer(d$stage)
  d$recallPercent <- as.numeric(d$recallPercent)
  d
}

stage_accuracy_table <- function(recallData, pass_threshold = 80.0) {
  if (is.null(recallData) || nrow(recallData) == 0L) {
    stop("recall data is empty.")
  }
  stages <- sort(unique(recallData$stage))
  out <- data.frame(
    stage = integer(),
    totalPatterns = integer(),
    passedPatterns = integer(),
    failedPatterns = integer(),
    accuracyPercent = numeric(),
    stringsAsFactors = FALSE
  )
  for (st in stages) {
    sd <- recallData[recallData$stage == st, , drop = FALSE]
    if (nrow(sd) == 0L) {
      next
    }
    pats <- unique(sd$patternId)
    ok <- 0L
    for (pid in pats) {
      pr <- sd[sd$patternId == pid, "recallPercent", drop = TRUE]
      last <- pr[length(pr)]
      if (last >= pass_threshold) {
        ok <- ok + 1L
      }
    }
    n <- length(pats)
    out <- rbind(out, data.frame(
      stage = st,
      totalPatterns = n,
      passedPatterns = ok,
      failedPatterns = n - ok,
      accuracyPercent = 100 * ok / n,
      stringsAsFactors = FALSE
    ))
  }
  out
}

safe_aic <- function(fit) {
  if (is.null(fit)) {
    return(NA_real_)
  }
  tryCatch(as.numeric(AIC(fit)), error = function(...) NA_real_)
}

clip_pct <- function(z) {
  ifelse(is.finite(z), pmin(100, pmax(0, z)), NA_real_)
}

fit_power_raw <- function(d) {
  if (nrow(d) < 3L) {
    return(NULL)
  }
  if (any(d$stage <= 0L)) {
    warning("Power law skipped: need strictly positive stages.", call. = FALSE)
    return(NULL)
  }
  y <- d$accuracyPercent
  s <- d$stage
  starts <- list(
    list(a = max(median(y), 1) / median(s)^0.5, b = 0.5),
    list(a = mean(y) / mean(s)^0.3, b = 0.3),
    list(a = y[1] / s[1]^0.5, b = 0.8)
  )
  for (st0 in starts) {
    fit <- tryCatch(
      nls(accuracyPercent ~ a * stage^b, data = d, start = st0, control = list(maxiter = 100)),
      error = function(e) NULL
    )
    if (!is.null(fit)) {
      return(fit)
    }
  }
  NULL
}

PASS_THRESHOLD <- 80.0
# Default: edit this path to switch conditions/stim/seed.
  RUN_FOLDER <- file.path(
    Sys.getenv("USERPROFILE"),
    "AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/geometric20_condA_seed1702"
  )

args <- commandArgs(trailingOnly = TRUE)
plain <- args[!grepl("^--", args)]
if (length(plain) >= 1L) {
  if (nzchar(plain[1L])) {
    RUN_FOLDER <- plain[1L]
  }
}
if (length(plain) >= 2L) {
  PASS_THRESHOLD <- as.numeric(plain[2L])
}

run_folder <- normalizePath(path.expand(RUN_FOLDER), winslash = "/", mustWork = TRUE)
recall <- read_recall_history(run_folder)
if (is.null(recall)) {
  stop("recall_history.csv missing or wrong columns in:\n  ", run_folder)
}
acc <- stage_accuracy_table(recall, PASS_THRESHOLD)
if (nrow(acc) < 2L) {
  stop("Need at least 2 stages.")
}

d <- acc[order(acc$stage), , drop = FALSE]
y <- d$accuracyPercent
st <- d$stage

fit_lin <- tryCatch(lm(accuracyPercent ~ stage, data = d), error = function(e) NULL)
fit_inv <- if (nrow(d) >= 3L && all(d$stage > 0L)) {
  tryCatch(lm(accuracyPercent ~ I(1 / stage), data = d), error = function(e) NULL)
} else {
  NULL
}
fit_pow <- fit_power_raw(d)
fit_sig <- if (nrow(d) >= 5L) {
  tryCatch(
    nls(accuracyPercent ~ SSfpl(stage, A, B, xmid, scal), data = d, control = list(maxiter = 200)),
    error = function(e) NULL
  )
} else {
  NULL
}

fits <- list(linear = fit_lin, inverse_stage = fit_inv, power_raw = fit_pow, sigmoid = fit_sig)

pred <- function(name, stages) {
  f <- fits[[name]]
  if (is.null(f)) {
    return(rep(NA_real_, length(stages)))
  }
  if (name == "linear") {
    return(clip_pct(predict(f, newdata = data.frame(stage = stages))))
  }
  if (name == "inverse_stage") {
    return(clip_pct(predict(f, newdata = data.frame(stage = stages))))
  }
  if (name == "power_raw") {
    return(clip_pct(predict(f, newdata = data.frame(stage = stages))))
  }
  if (name == "sigmoid") {
    return(clip_pct(predict(f, newdata = data.frame(stage = stages))))
  }
  rep(NA_real_, length(stages))
}

rmse_pct <- function(y, yhat) {
  ok <- is.finite(y) & is.finite(yhat)
  if (!any(ok)) {
    return(NA_real_)
  }
  sqrt(mean((y[ok] - yhat[ok])^2))
}

tab <- data.frame(
  model = c("linear", "inverse_stage", "power_raw", "sigmoid"),
  k = c(
    if (!is.null(fit_lin)) length(coef(fit_lin)) else NA_integer_,
    if (!is.null(fit_inv)) length(coef(fit_inv)) else NA_integer_,
    if (!is.null(fit_pow)) length(coef(fit_pow)) else NA_integer_,
    if (!is.null(fit_sig)) length(coef(fit_sig)) else NA_integer_
  ),
  AIC = c(safe_aic(fit_lin), safe_aic(fit_inv), safe_aic(fit_pow), safe_aic(fit_sig)),
  RMSE_pct = c(
    rmse_pct(y, pred("linear", st)),
    rmse_pct(y, pred("inverse_stage", st)),
    rmse_pct(y, pred("power_raw", st)),
    rmse_pct(y, pred("sigmoid", st))
  ),
  stringsAsFactors = FALSE
)
tab <- tab[order(tab$AIC, na.last = TRUE), , drop = FALSE]

message("Run: ", basename(run_folder), "\n")
message("Models: linear; inverse stage (1/stage); power y = a * stage^b; sigmoid (SSfpl).\n")
print(tab)

xs <- seq(min(d$stage), max(d$stage), length.out = 80L)
curve_df <- rbind(
  data.frame(stage = xs, accuracyPercent = pred("linear", xs), model = "linear"),
  data.frame(stage = xs, accuracyPercent = pred("inverse_stage", xs), model = "inverse_stage"),
  data.frame(stage = xs, accuracyPercent = pred("power_raw", xs), model = "power_raw"),
  data.frame(stage = xs, accuracyPercent = pred("sigmoid", xs), model = "sigmoid")
)
curve_df <- curve_df[is.finite(curve_df$accuracyPercent), , drop = FALSE]

p <- ggplot(d, aes(stage, accuracyPercent)) +
  geom_point(size = 3) +
  geom_line(linewidth = 1.0, color = "steelblue", alpha = 0.75) +
  geom_text(aes(label = paste0(passedPatterns, "/", totalPatterns)), vjust = -1.0, size = 3.5, color = "gray40") +
  geom_line(aes(stage, accuracyPercent, color = model), data = curve_df, linewidth = 0.8, inherit.aes = FALSE) +
  labs(
    title = "Overall Accuracy: Percentage of Patterns Successfully Retrieved",
    subtitle = paste0(
      "Shows how many patterns passed (\u2265", PASS_THRESHOLD, "%) out of total patterns learned at each stage"
    ),
    x = "Number of Patterns Learned (Stage)",
    y = "Accuracy (%)"
  ) +
  ylim(0, 100) +
  theme_minimal()

if (interactive()) {
  print(p)
} else {
  message("Note: plots only display in interactive R (RStudio/R GUI).")
}

invisible(list(accuracy = d, table = tab, fits = fits, plot = p))
