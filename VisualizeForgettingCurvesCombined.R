# ============================================================================
# Combined Forgetting Curves Visualization
# ============================================================================
# This script creates a single plot showing all forgetting curves together,
# with each pattern's recall performance as more patterns are learned.
#
# Usage:
#   source("VisualizeForgettingCurvesCombined.R")
#   experimentFolder <- "path/to/experiment/folder"
#   result <- createCombinedForgettingCurves(experimentFolder)
# ============================================================================

library(ggplot2)
library(dplyr)

# ============================================================================
# Configuration
# ============================================================================

# Set your experiment folder path here
experimentFolder <- "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed827"

# ============================================================================
# Helper Functions
# ============================================================================

# Try to read Unity's recall_history.csv if it exists
readRecallHistory <- function(folder) {
    historyFile <- file.path(folder, "recall_history.csv")
    if (file.exists(historyFile)) {
        cat("Found recall_history.csv - using Unity's calculated recall values\n")
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

# ============================================================================
# Main Function: Create Combined Forgetting Curves
# ============================================================================

createCombinedForgettingCurves <- function(folder = experimentFolder,
                                           savePlot = TRUE,
                                           showPassThreshold = NULL,
                                           plotWidth = 14,
                                           plotHeight = 8) {
    
    cat("============================================================================\n")
    cat("Creating Combined Forgetting Curves\n")
    cat("============================================================================\n")
    cat("Folder:", folder, "\n\n")
    
    # Try to read Unity's recall_history.csv
    recallData <- readRecallHistory(folder)
    
    if (is.null(recallData) || nrow(recallData) == 0) {
        stop("recall_history.csv not found. Please run an experiment with cumulative recall mode enabled in Unity.")
    }
    
    cat("Loaded", nrow(recallData), "recall test results\n")
    
    # Get unique patterns and sort by number
    uniquePatterns <- unique(recallData$patternId)
    patternNums <- sapply(uniquePatterns, extractPatternNum)
    sortedIndices <- order(patternNums)
    sortedPatterns <- uniquePatterns[sortedIndices]
    
    cat("Patterns:", paste(sortedPatterns, collapse = ", "), "\n\n")
    
    # Show summary of data
    cat("=== Data Summary ===\n")
    for (patId in sortedPatterns) {
        patternData <- recallData[recallData$patternId == patId, ]
        if (nrow(patternData) > 0) {
            cat("  ", patId, ": ", nrow(patternData), " test(s) across stages ", 
                min(patternData$stage), "-", max(patternData$stage),
                " (recall: ", sprintf("%.1f", min(patternData$recallPercent)), 
                "%-", sprintf("%.1f", max(patternData$recallPercent)), "%)\n", sep = "")
        }
    }
    cat("\n")
    
    # Create color palette
    nPatterns <- length(sortedPatterns)
    if (nPatterns <= 10) {
        if (!require("RColorBrewer", quietly = TRUE)) {
            install.packages("RColorBrewer")
            library(RColorBrewer)
        }
        colors <- RColorBrewer::brewer.pal(max(3, nPatterns), "Set1")
        if (nPatterns < length(colors)) {
            colors <- colors[1:nPatterns]
        }
    } else {
        colors <- rainbow(nPatterns)
    }
    names(colors) <- sortedPatterns
    
    cat("=== Creating Combined Plot ===\n")
    
    # Create the combined plot
    p <- ggplot(recallData, aes(x = stage, y = recallPercent, color = patternId)) +
        geom_line(linewidth = 1.3, alpha = 0.85) +
        geom_point(size = 3, alpha = 0.9) +
        scale_color_manual(values = colors, name = "Pattern") +
        labs(
            title = "Forgetting Curves: Recall Performance as Patterns Are Added",
            subtitle = "Each line shows how a pattern's recall degrades as more patterns are learned",
            x = "Number of Patterns Learned (Stage)",
            y = "Recall Percentage (%)",
            color = "Pattern ID"
        ) +
        theme_minimal() +
        theme(
            plot.title = element_text(size = 16, face = "bold", hjust = 0.5),
            plot.subtitle = element_text(size = 12, hjust = 0.5, margin = margin(b = 20)),
            axis.title = element_text(size = 13),
            axis.text = element_text(size = 11),
            legend.title = element_text(size = 12, face = "bold"),
            legend.text = element_text(size = 11),
            legend.position = "right",
            panel.grid.minor = element_blank(),
            panel.grid.major = element_line(color = "gray90", linewidth = 0.5),
            plot.margin = margin(20, 20, 20, 20)
        ) +
        scale_x_continuous(breaks = unique(recallData$stage), minor_breaks = NULL) +
        scale_y_continuous(limits = c(0, 100), breaks = seq(0, 100, 20))
    
    # Add pass threshold line if provided
    if (!is.null(showPassThreshold) && is.numeric(showPassThreshold)) {
        p <- p +
            geom_hline(yintercept = showPassThreshold, linetype = "dashed", 
                      color = "gray50", linewidth = 0.9, alpha = 0.7) +
            annotate("text", x = max(recallData$stage), y = showPassThreshold + 2,
                    label = paste("Pass Threshold:", showPassThreshold, "%"),
                    hjust = 1, vjust = 0, size = 4, color = "gray40")
    }
    
    # Print plot
    print(p)
    
    # Save plot
    if (savePlot) {
        plotFile <- file.path(folder, "forgetting_curves_combined.png")
        ggsave(plotFile, plot = p, width = plotWidth, height = plotHeight, dpi = 300)
        cat("Saved combined plot to:", plotFile, "\n")
    }
    
    # Create summary table
    cat("\n=== Forgetting Curve Summary ===\n")
    summaryTable <- recallData %>%
        group_by(patternId) %>%
        summarise(
            FirstRecall = first(recallPercent),
            LastRecall = last(recallPercent),
            BestRecall = max(recallPercent),
            WorstRecall = min(recallPercent),
            Decline = first(recallPercent) - last(recallPercent),
            Tests = n(),
            .groups = "drop"
        ) %>%
        arrange(extractPatternNum(patternId))
    
    print(summaryTable)
    
    # Save summary
    if (savePlot) {
        summaryFile <- file.path(folder, "forgetting_curves_combined_summary.csv")
        write.csv(summaryTable, summaryFile, row.names = FALSE)
        cat("\nSaved summary to:", summaryFile, "\n")
    }
    
    return(list(plot = p, data = recallData, summary = summaryTable))
}

# ============================================================================
# Quick Start
# ============================================================================

# Uncomment and modify to run:
# result <- createCombinedForgettingCurves(
#     folder = "C:/Users/Mak/AppData/LocalLow/DefaultCompany/Attractors/CSVExperimentLogs/_seed827",
#     showPassThreshold = 66.6,
#     plotWidth = 14,
#     plotHeight = 8
# )
