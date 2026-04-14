#   • R console:  setwd("C:/Users/Mak/Attractors")
#                 source("overall_accuracy_models_synthetic_tests.R")
# 
#
# fake datasets: linear fit, sigmoid fit, step fit.
# ------------------------------------------------------------------------------

par(mfrow = c(3, 1), mar = c(4, 4, 3, 1))

# ----- 1) Linear: lm + abline -----
set.seed(1)
stage <- 1:8
accuracy <- c(25, 30, 36, 41, 47, 52, 56, 60) + rnorm(8, sd = 1.2)
fit_line <- lm(accuracy ~ stage)
plot(stage, accuracy, pch = 19, xlab = "Stage", ylab = "Accuracy (fake %)",
     main = "1) Linear best fit")
abline(fit_line, col = "red", lwd = 2)
stopifnot(coef(fit_line)["stage"] > 0)

# ----- 2) Sigmoid: nls with built-in SSfpl (4-parameter logistic) -----
set.seed(2)
x <- 1:15
y <- 15 + 80 / (1 + exp(-(x - 7.5) / 1.8)) + rnorm(length(x), sd = 2)
y <- pmin(100, pmax(0, y))
fit_sig <- nls(y ~ SSfpl(x, A, B, xmid, scal), data = data.frame(x = x, y = y))
plot(x, y, pch = 19, xlab = "Stage", ylab = "Accuracy (fake %)",
     main = "2) Sigmoid best fit (SSfpl)")
xs <- seq(min(x), max(x), length.out = 80)
lines(xs, predict(fit_sig, newdata = data.frame(x = xs)), col = "red", lwd = 2)
stopifnot(coef(fit_sig)["B"] > coef(fit_sig)["A"])

# ----- 3) Step: one accuracy level per stage (factor) + step-shaped line -----
set.seed(3)
stage3 <- 1:10
accuracy3 <- c(rep(38, 5), rep(75, 5)) + rnorm(10, sd = 1.5)
fit_step <- lm(accuracy3 ~ factor(stage3))
plot(stage3, accuracy3, pch = 19, xlab = "Stage", ylab = "Accuracy (fake %)",
     main = "3) Step fit (constant per stage)")
ord <- order(stage3)
fv <- predict(fit_step, newdata = data.frame(stage3 = stage3[ord]))
lines(stage3[ord], fv, type = "s", col = "red", lwd = 2)
stopifnot(sqrt(mean(residuals(fit_step)^2)) < 2)

par(mfrow = c(1, 1))

message("All three tiny demos ran OK.")
