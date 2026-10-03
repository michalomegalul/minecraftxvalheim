package dev.michal.valcraft;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * TCP link to the Valheim plugin (which listens on localhost). One JSON object per line.
 * Reconnects in the background; sending while disconnected just drops the message.
 */
public final class Link {
	public static final int PORT = 25610;

	private final ConcurrentLinkedQueue<JsonObject> inbox = new ConcurrentLinkedQueue<>();
	private volatile Socket socket;
	private volatile OutputStream out;

	public void start() {
		Thread t = new Thread(this::run, "Valcraft link");
		t.setDaemon(true);
		t.start();
	}

	public boolean isConnected() {
		return out != null;
	}

	/** Messages received from Valheim, drained on the client thread. */
	public JsonObject poll() {
		return inbox.poll();
	}

	public void send(JsonObject msg) {
		OutputStream o = out;
		if (o == null) return;
		try {
			o.write((msg.toString() + "\n").getBytes(StandardCharsets.UTF_8));
		} catch (Exception e) {
			close();
		}
	}

	private void run() {
		while (true) {
			try (Socket s = new Socket()) {
				s.connect(new InetSocketAddress("127.0.0.1", PORT), 1000);
				s.setTcpNoDelay(true);
				socket = s;
				out = s.getOutputStream();
				inbox.add(status("connected"));
				BufferedReader in = new BufferedReader(new InputStreamReader(s.getInputStream(), StandardCharsets.UTF_8));
				String line;
				while ((line = in.readLine()) != null) {
					try {
						inbox.add(JsonParser.parseString(line).getAsJsonObject());
					} catch (Exception ignored) {
						// Malformed line from Valheim; skip it.
					}
				}
			} catch (Exception ignored) {
				// Valheim not running yet, or the connection dropped.
			}
			if (out != null) inbox.add(status("disconnected"));
			close();
			try {
				Thread.sleep(2000);
			} catch (InterruptedException e) {
				return;
			}
		}
	}

	private void close() {
		out = null;
		Socket s = socket;
		socket = null;
		if (s != null) {
			try {
				s.close();
			} catch (Exception ignored) {
			}
		}
	}

	private static JsonObject status(String state) {
		JsonObject o = new JsonObject();
		o.addProperty("t", "_link");
		o.addProperty("state", state);
		return o;
	}
}
