import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import "./App.css";

console.log("[JS LOG] main.jsx execution started");

ReactDOM.createRoot(document.getElementById("root")).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);

