# ============================================================================
# Overall Accuracy Visualization
# ============================================================================
# This script plots the overall accuracy (percentage of patterns successfully
# retrieved) at each stage as more patterns are learned.
#
# Usage (run these yourself after source(); do not leave them uncommented here
#       or sourcing this file will call source() recursively and overflow the stack):
#   setwd("C:/Users/Mak/Attractors")
  # source("VisualizeOverallAccuracy.R")
  # result <- createOverallAccuracyPlot(
  #     folder = "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed226",
  #     passThreshold = 80.0
  # )
# ============================================================================

library(ggplot2)
library(dplyr)

# ============================================================================
# Helper Functions
# ============================================================================

# Try to read Unity's recall_history.csv if it exists
readRecallHistory <- function(folder, quiet = FALSE) {
    historyFile <- file.path(folder, "recall_history.csv")
    if (file.exists(historyFile)) {
        if (!isTRUE(quiet)) {
            cat("Found recall_history.csv - using Unity's calculated recall values\n")
        }
        data <- read.csv(historyFile, stringsAsFactors = FALSE)
        # Ensure correct column types
        if ("patternId" %in% colnames(data) && "stage" %in% colnames(data) && 
            "recallPercent" %in% colnames(data)) {
            data$patternId <- as.character(data$patternId)
            data$stage <- as.integer(data$stage)
            data$recallPercent <- as.numeric(data$recallPercent)
            if ("testNumber" %in% colnames(data)) {
                data$testNumber <- as.integer(data$testNumber)
            } else {
                data$testNumber <- 1
            }
            return(data)
        }
    }
    return(NULL)
}

# Extract pattern number for ordering
extractPatternNum <- function(patId) {
    numMatch <- regmatches(patId, regexpr("\\d+", patId))
    if (length(numMatch) > 0) {
        return(as.numeric(numMatch[1]))
    }
    return(999)
}

#' Stage-wise overall accuracy from recall_history rows (cumulative recall layout).
#' @param recallData Data frame with patternId, stage, recallPercent.
#' @param passThreshold Recall % at or above counts as pass for accuracy denominator.
#' @param verbose If TRUE, print per-stage breakdown (same as legacy createOverallAccuracyPlot).
#' @return Data frame: stage, totalPatterns, passedPatterns, accuracyPercent.
compute_overall_accuracy_from_recalls <- function(recallData, passThreshold = 80.0, verbose = TRUE) {
    if (is.null(recallData) || nrow(recallData) == 0L) {
        stop("recallData is empty.")
    }
    if (verbose) {
        uniquePatterns <- unique(recallData$patternId)
        patternNums <- sapply(uniquePatterns, extractPatternNum)
        sortedIndices <- order(patternNums)
        sortedPatterns <- uniquePatterns[sortedIndices]
        cat("Patterns:", paste(sortedPatterns, collapse = ", "), "\n\n")
        allStages <- sort(unique(recallData$stage))
        cat("Stages:", paste(allStages, collapse = ", "), "\n\n")
        cat("=== Calculating Accuracy for Each Stage ===\n")
    }

    allStages <- sort(unique(recallData$stage))
    accuracyData <- data.frame(
        stage = integer(),
        totalPatterns = integer(),
        passedPatterns = integer(),
        accuracyPercent = numeric(),
        stringsAsFactors = FALSE
    )

    for (stage in allStages) {
        stageData <- recallData[recallData$stage == stage, ]
        if (nrow(stageData) == 0L) {
            next
        }
        stagePatterns <- unique(stageData$patternId)
        patternRecalls <- data.frame(
            patternId = character(length(stagePatterns)),
            recallPercent = numeric(length(stagePatterns)),
            stringsAsFactors = FALSE
        )
        for (j in seq_along(stagePatterns)) {
            patId <- stagePatterns[j]
            patData <- stageData[stageData$patternId == patId, ]
            patternRecalls$patternId[j] <- patId
            patternRecalls$recallPercent[j] <- patData$recallPercent[nrow(patData)]
        }
        totalPatterns <- nrow(patternRecalls)
        passedPatterns <- sum(patternRecalls$recallPercent >= passThreshold)
        accuracyPercent <- (passedPatterns / totalPatterns) * 100
        if (verbose) {
            cat(sprintf("Stage %d: %d/%d patterns passed (%.1f%% accuracy)\n",
                stage, passedPatterns, totalPatterns, accuracyPercent))
            for (i in seq_len(nrow(patternRecalls))) {
                patId <- patternRecalls$patternId[i]
                recall <- patternRecalls$recallPercent[i]
                status <- if (recall >= passThreshold) "✓" else "✗"
                cat(sprintf("  %s %s: %.1f%%\n", status, patId, recall))
            }
        }
        accuracyData <- rbind(accuracyData, data.frame(
            stage = stage,
            totalPatterns = totalPatterns,
            passedPatterns = passedPatterns,
            accuracyPercent = accuracyPercent
        ))
    }
    if (verbose) {
        cat("\n")
    }
    accuracyData
}

# ============================================================================
# Main Function: Create Overall Accuracy Plot
# ============================================================================

createOverallAccuracyPlot <- function(folder, passThreshold = 80.0, savePlot = TRUE,
                                      plotWidth = 12, plotHeight = 7) {
    
    cat("============================================================================\n")
    cat("Creating Overall Accuracy Plot\n")
    cat("============================================================================\n")
    cat("Folder:", folder, "\n")
    cat("Pass Threshold:", passThreshold, "%\n\n")
    
    # Try to read Unity's recall_history.csv
    recallData <- readRecallHistory(folder)
    
    if (is.null(recallData) || nrow(recallData) == 0) {
        stop("recall_history.csv not found. Please run an experiment with cumulative recall mode enabled in Unity.")
    }
    
    cat("Loaded", nrow(recallData), "recall test results\n\n")

    accuracyData <- compute_overall_accuracy_from_recalls(recallData, passThreshold, verbose = TRUE)
    
    # Create the plot
    cat("=== Creating Accuracy Plot ===\n")
    
    p <- ggplot(accuracyData, aes(x = stage, y = accuracyPercent)) +
        geom_line(linewidth = 1.5, color = "steelblue", alpha = 0.8) +
        geom_point(size = 4, color = "steelblue", alpha = 0.9) +
        geom_text(aes(label = paste0(passedPatterns, "/", totalPatterns)), 
                 vjust = -1.2, hjust = 0.5, size = 3.5, color = "gray40") +
        labs(
            title = "Overall Accuracy: Percentage of Patterns Successfully Retrieved",
            subtitle = paste0("Shows how many patterns passed (≥", passThreshold, "%) out of total patterns learned at each stage"),
            x = "Number of Patterns Learned (Stage)",
            y = "Accuracy (%)",
            caption = paste0("Pass threshold: ", passThreshold, "%")
        ) +
        theme_minimal() +
        theme(
            plot.title = element_text(size = 16, face = "bold", hjust = 0.5),
            plot.subtitle = element_text(size = 12, hjust = 0.5, margin = margin(b = 15)),
            plot.caption = element_text(size = 10, hjust = 1, color = "gray50"),
            axis.title = element_text(size = 13),
            axis.text = element_text(size = 11),
            panel.grid.minor = element_blank(),
            panel.grid.major = element_line(color = "gray90", linewidth = 0.5),
            plot.margin = margin(20, 20, 20, 20)
        ) +
        scale_x_continuous(breaks = accuracyData$stage, minor_breaks = NULL) +
        scale_y_continuous(limits = c(0, 100), breaks = seq(0, 100, 20))
    
    # Add a horizontal line at 100% for reference
    p <- p + geom_hline(yintercept = 100, linetype = "dashed", 
                       color = "gray70", linewidth = 0.7, alpha = 0.5)
    
    # Print plot
    print(p)
    
    # Save plot
    if (savePlot) {
        plotFile <- file.path(folder, "overall_accuracy.png")
        ggsave(plotFile, plot = p, width = plotWidth, height = plotHeight, dpi = 300)
        cat("Saved plot to:", plotFile, "\n")
    }
    
    # Create summary table
    cat("\n=== Accuracy Summary ===\n")
    print(accuracyData)
    
    # Save summary
    if (savePlot) {
        summaryFile <- file.path(folder, "overall_accuracy_summary.csv")
        write.csv(accuracyData, summaryFile, row.names = FALSE)
        cat("\nSaved summary to:", summaryFile, "\n")
    }
    
    # Calculate overall statistics
    cat("\n=== Overall Statistics ===\n")
    cat(sprintf("Average Accuracy: %.1f%%\n", mean(accuracyData$accuracyPercent)))
    cat(sprintf("Min Accuracy: %.1f%% (at stage %d)\n", 
               min(accuracyData$accuracyPercent), 
               accuracyData$stage[which.min(accuracyData$accuracyPercent)]))
    cat(sprintf("Max Accuracy: %.1f%% (at stage %d)\n", 
               max(accuracyData$accuracyPercent),
               accuracyData$stage[which.max(accuracyData$accuracyPercent)]))
    cat(sprintf("Final Accuracy: %.1f%% (at stage %d)\n",
               tail(accuracyData$accuracyPercent, 1),
               tail(accuracyData$stage, 1)))
    
    return(list(plot = p, data = accuracyData))
}
