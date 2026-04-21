suppressPackageStartupMessages({
  library(ggplot2)
})

# ----------------------------- USER SETTINGS ----------------------------------
# Provide up to 4 folders (one per condition panel). Each folder must contain:
#   - recall_test_<NN>_*.csv
#   - recall_test_<NN>_intended_*.csv
#
# Example (update these to your run folders):
FOLDERS <- c(
  Neutral = "/Users/makenzygilbert/OpenDynamicsUnity/radials_neutral_seed1702",
  CondA = "/Users/makenzygilbert/OpenDynamicsUnity/radials_neutral_seed1702",
  CondB = "/Users/makenzygilbert/OpenDynamicsUnity/radials_neutral_seed1702",
  CondC = "/Users/makenzygilbert/OpenDynamicsUnity/radials_neutral_seed1702"
)

# Which pattern to plot (matches recall_test_XX_*.csv)
PATTERN_NUM <- 1

# If you want a Cue OFF segment like the example figure.
CUE_OFF_AFTER_FRACTION <- 0.25  # set to NA to disable cue segmentation

# Output PNG (set to "" to skip saving)
OUT_PNG <- "example_trajectories_panel.png"
OUT_WIDTH <- 12
OUT_HEIGHT <- 4.2
# ------------------------------------------------------------------------------

pad2 <- function(x) sprintf("%02d", as.integer(x))

latest_matching_file <- function(folder, pattern) {
  pat <- paste0("^recall_test_", pad2(pattern), "_\\d{8}_\\d{6}\\.csv$")
  ff <- list.files(folder, pattern = pat, full.names = TRUE)
  if (length(ff) == 0L) return(NA_character_)
  ff[which.max(file.info(ff)$mtime)]
}

latest_matching_intended <- function(folder, pattern) {
  pat <- paste0("^recall_test_", pad2(pattern), "_intended_\\d{8}_\\d{6}\\.csv$")
  ff <- list.files(folder, pattern = pat, full.names = TRUE)
  if (length(ff) == 0L) return(NA_character_)
  ff[which.max(file.info(ff)$mtime)]
}

read_actual_path <- function(path_csv) {
  d <- read.csv(path_csv, stringsAsFactors = FALSE)
  need <- c("time", "x", "y")
  if (!all(need %in% names(d))) {
    stop("Actual path missing columns: ", paste(need, collapse = ", "), "\nFile: ", path_csv)
  }
  d$idx <- seq_len(nrow(d))
  d
}

read_intended_path <- function(intended_csv) {
  d <- read.csv(intended_csv, stringsAsFactors = FALSE)
  need <- c("x", "y")
  if (!all(need %in% names(d))) {
    stop("Intended path missing x,y columns.\nFile: ", intended_csv)
  }
  d$idx <- seq_len(nrow(d))
  d
}

add_cue_phase <- function(d, cue_off_after_fraction = CUE_OFF_AFTER_FRACTION) {
  if (!is.finite(cue_off_after_fraction)) {
    d$cue_phase <- "Cue ON"
    return(d)
  }
  cut <- ceiling(nrow(d) * cue_off_after_fraction)
  d$cue_phase <- ifelse(d$idx <= cut, "Cue ON", paste0("Cue OFF (after ", round(cue_off_after_fraction * 100), "%)"))
  d
}

load_one_condition <- function(name, folder, pattern_num) {
  actual_csv <- latest_matching_file(folder, pattern_num)
  intended_csv <- latest_matching_intended(folder, pattern_num)
  if (!nzchar(actual_csv) || is.na(actual_csv)) {
    stop("No recall_test file found in: ", folder)
  }
  if (!nzchar(intended_csv) || is.na(intended_csv)) {
    stop("No intended file found in: ", folder)
  }

  actual <- read_actual_path(actual_csv)
  actual <- add_cue_phase(actual)
  intended <- read_intended_path(intended_csv)

  start <- actual[1, c("x", "y"), drop = FALSE]
  target <- intended[nrow(intended), c("x", "y"), drop = FALSE]

  actual$condition <- name
  intended$condition <- name
  start$condition <- name
  target$condition <- name

  list(actual = actual, intended = intended, start = start, target = target,
       actual_csv = actual_csv, intended_csv = intended_csv)
}

make_panel_plot <- function(folders = FOLDERS, pattern_num = PATTERN_NUM) {
  nm <- names(folders)
  if (is.null(nm) || any(!nzchar(nm))) {
    stop("FOLDERS must be a *named* character vector, e.g. c(Neutral='...', CondA='...').")
  }

  loaded <- lapply(nm, function(n) load_one_condition(n, folders[[n]], pattern_num))
  actual_df <- do.call(rbind, lapply(loaded, `[[`, "actual"))
  intended_df <- do.call(rbind, lapply(loaded, `[[`, "intended"))
  start_df <- do.call(rbind, lapply(loaded, `[[`, "start"))
  target_df <- do.call(rbind, lapply(loaded, `[[`, "target"))

  # Build segments for actual trajectory so linetype can change at cue switch.
  actual_df <- actual_df[order(actual_df$condition, actual_df$idx), , drop = FALSE]
  actual_df$seg <- ave(actual_df$idx, actual_df$condition, actual_df$cue_phase, FUN = seq_along)

  ggplot() +
    geom_path(
      data = intended_df,
      aes(x, y),
      color = "gray70",
      linewidth = 1.1,
      alpha = 0.85
    ) +
    geom_path(
      data = actual_df,
      aes(x, y, linetype = cue_phase, group = interaction(condition, cue_phase)),
      color = "black",
      linewidth = 1.1
    ) +
    geom_point(data = start_df, aes(x, y), color = "forestgreen", size = 3) +
    geom_point(data = target_df, aes(x, y), color = "red3", shape = 8, size = 3.5) +
    facet_wrap(~ condition, nrow = 1) +
    coord_equal() +
    labs(
      title = paste0("Example Trajectories (Pattern ", pad2(pattern_num), ")"),
      subtitle = "Gray: intended path  |  Black: actual trajectory",
      x = NULL,
      y = NULL,
      linetype = NULL
    ) +
    theme_minimal(base_size = 12) +
    theme(
      text = element_text(face = "bold"),
      plot.title = element_text(face = "bold"),
      plot.subtitle = element_text(face = "bold"),
      legend.text = element_text(face = "bold"),
      strip.text = element_text(face = "bold"),
      panel.grid = element_blank(),
      axis.text = element_blank(),
      axis.ticks = element_blank(),
      legend.position = "bottom",
      legend.title = element_text(face = "bold")
    )
}

p <- make_panel_plot()
print(p)

if (nzchar(OUT_PNG)) {
  ggsave(OUT_PNG, plot = p, width = OUT_WIDTH, height = OUT_HEIGHT, dpi = 300)
  message("Wrote ", OUT_PNG)
}

